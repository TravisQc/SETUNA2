# .NET 8 Dependency Compatibility

This record is part of the `modernize-dpi-scaling` migration. A package is not
considered compatible merely because restore succeeds; its selected asset and a
format-level smoke test must be recorded before release.

| Package | Version | Selected asset | Decision | Verification |
| --- | --- | --- | --- | --- |
| `Newtonsoft.Json` | 13.0.4 | `lib/net6.0` | Proceed | Option XML and pipe JSON tests |
| `Svg` | 3.4.8 | `lib/net8.0` | Proceed | Existing SVG rasterization suite |
| `ExCSS` (via `Svg`) | 4.2.3 | `lib/net7.0` | Proceed | Transitive; exercised by the SVG suite |
| `System.Drawing.Common` (via `Svg`) | 5.0.3 | `ref/netcoreapp3.0` | Proceed | Conflict-resolved to the WindowsDesktop framework assembly at compile time |
| `System.Configuration.ConfigurationManager` | 8.0.1 | `lib/net8.0` | Proceed | Settings load and runtime configuration tests |
| `Prowl.Aperture` | 3.3.0 | `lib/net8.0` (`Aperture.dll`) | Replaces `System.Drawing.PSD` | Dependency-free MIT decoder; `PsdBitmapDecoderTests` verifies flattened PSD pixels. |
| `System.Drawing.PSD` | removed | none | Replace | net40-only asset; `PsdBitmapDecoder` is the adapter |
| `MSTest.TestFramework` / `MSTest.TestAdapter` | 3.6.1 | `lib/net8.0` | Proceed | 249 tests discovered and passing |
| `Microsoft.NET.Test.Sdk` | 17.11.1 | `lib/netcoreapp3.1` testhost | Proceed | VSTest host for `dotnet test` |
| `TgaLib` | removed | none | Replace | `TgaBitmapDecoder` is the in-process adapter with dedicated format tests |
| `Costura.Fody` | removed | none | Do not restore | SDK single-file publish owns managed bundling |

## PSD decision

`System.Drawing.PSD 0.1.0` exposes only a `net40` asset and produced `NU1701`.
PSD files now go through `PsdBitmapDecoder`, backed by the native `net8.0` asset
in `Prowl.Aperture`. The adapter requests RGBA8 flattened pixels and copies them
to an owned `Format32bppArgb` bitmap. The old package is gone from the graph
entirely. Architecture-specific publish validation remains part of task 9.4.

`Prowl.Aperture` is worth a second look before cutover: it is MIT-licensed,
dependency-free and published from `github.com/ProwlEngine/Anthology`, but 3.3.0
is its only version on nuget.org and it is an image loader for a game engine
rather than a PSD library. It is a small, replaceable seam — `PsdBitmapDecoder`
is the only caller — so the fallback if it is abandoned is another decoder
behind the same adapter, not a rework.

## Version availability

Every pinned version above resolves on nuget.org: `Newtonsoft.Json 13.0.4`,
`Svg 3.4.8`, `System.Configuration.ConfigurationManager 8.0.1`,
`Prowl.Aperture 3.3.0`, `Microsoft.NET.Test.Sdk 17.11.1` and
`MSTest.TestAdapter`/`MSTest.TestFramework 3.6.1`.

## Restore and build commands

.NET SDK 8.0.424. One restore covers both platforms because the projects declare
`RuntimeIdentifiers=win-x86;win-x64`.

```
dotnet restore SETUNA.sln
dotnet build   SETUNA.sln -c Debug -p:Platform=x64
dotnet test    SETUNATests/SETUNATests.csproj -c Debug -p:Platform=x64 --no-build
```

`./scripts/verify-build.ps1 -Configuration Debug -Platform x64` runs all three and
prints the artifact paths.

Status: clean restore with no NU warnings, both projects build for x86 and x64 in
Debug and Release, and 249 tests pass. There is no net48 project left to compare
against; rollback is a git revert of this change.

## Known net8 build warnings

Not errors, but each one is a real migration item rather than noise:

- `MSB3825` ×2 — `Resources\Image.resx` stores `SampleImage` and `Crypt` through
  `BinaryFormatter` (`application/x-microsoft.net.object.binary.base64`).
  BinaryFormatter is removed in .NET 9, so these two entries have to move to the
  `bytearray.base64` form with an explicit `System.Drawing.Bitmap` type before the
  runtime is bumped again. Nothing in the current suite loads them, so the failure
  mode is untested — task 9.4 should exercise both.
- `CA1416` ×~3900 — Windows-only API call sites flagged against the declared
  `SupportedOSPlatformVersion` of 10.0.17763. Expected for a WinForms port; suppress
  at the project level rather than annotating call sites.
- `SYSLIB0014` ×10 (`WebRequest`/`WebClient`), `SYSLIB0021` ×2 (derived crypto types),
  `SYSLIB0006` ×2 (`Thread.Abort`), `CS0672`/`CS0618` ×8 each — obsolete APIs in the
  Picasa uploader and DES helper. They compile and run on .NET 8; they are the
  candidates for the next cleanup pass, not blockers for this change.
