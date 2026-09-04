[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x86', 'x64'),
    # Both variants by default: that pair is what a release ships.
    #   selfcontained  carries the runtime, needs nothing installed
    #   portable       framework-dependent, needs the .NET 8 Desktop Runtime of the
    #                  same architecture, roughly a third of the size
    [ValidateSet('selfcontained', 'portable')]
    [string[]] $RuntimeMode = @('selfcontained', 'portable'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repositoryRoot 'SETUNA\SETUNA.csproj'

# The framework the portable artifacts bind to. A framework-dependent app rolls forward
# only within its own major version by default, so 9.x on the machine does not satisfy an
# 8.0 app.
$requiredRuntimeMajor = 8

# Mirrors ReleaseArtifactSuffix in SETUNA.csproj. The self-contained artifact deliberately
# keeps its historic unsuffixed name so links to published releases keep working.
function Get-ArtifactSuffix {
    param([string] $Mode)

    if ($Mode -eq 'portable') { return '_Portable' }
    return ''
}

# Where a .NET installation of a given architecture lives. ProgramW6432 and
# ProgramFiles(x86) are used rather than ProgramFiles because the latter follows the
# bitness of the PowerShell host, not the architecture being asked about.
function Get-DotnetRoots {
    param([string] $TargetPlatform)

    $roots = New-Object System.Collections.Generic.List[string]

    if ($TargetPlatform -eq 'x86') {
        if (${env:DOTNET_ROOT(x86)}) { $roots.Add(${env:DOTNET_ROOT(x86)}) }
        if (${env:ProgramFiles(x86)}) { $roots.Add((Join-Path ${env:ProgramFiles(x86)} 'dotnet')) }
    }
    else {
        if ($env:DOTNET_ROOT) { $roots.Add($env:DOTNET_ROOT) }
        if ($env:ProgramW6432) { $roots.Add((Join-Path $env:ProgramW6432 'dotnet')) }
        elseif ($env:ProgramFiles) { $roots.Add((Join-Path $env:ProgramFiles 'dotnet')) }
    }

    return $roots
}

function Find-DesktopRuntime {
    param([string] $TargetPlatform, [int] $Major)

    foreach ($root in Get-DotnetRoots $TargetPlatform) {
        $shared = Join-Path $root 'shared\Microsoft.WindowsDesktop.App'
        if (-not (Test-Path -LiteralPath $shared)) { continue }

        $match = Get-ChildItem -LiteralPath $shared -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "$Major.*" } |
            Sort-Object Name -Descending |
            Select-Object -First 1

        if ($match) { return $match.FullName }
    }

    return $null
}

$skipped = New-Object System.Collections.Generic.List[string]

# The publish artifact is the deliverable, so the checks below run against a copy of it
# alone in an empty directory — the situation a user is in after downloading one file.
# Running them in `publish\` would pass on companion files that happen to be siblings.
foreach ($targetPlatform in $Platform) {
    foreach ($mode in $RuntimeMode) {
        Write-Host "--- $Configuration $targetPlatform $mode ---"

        # -restore in the same invocation, per publish, on purpose. A publish evaluates with
        # SelfContained/PublishSingleFile and the platform's RuntimeIdentifier as global
        # properties, and a restore triggered under those can leave project.assets.json carrying
        # only that RID — after which the other platform fails NETSDK1047. Restoring immediately
        # before each publish, with the properties that publish will use, makes the order of the
        # platforms and the modes irrelevant.
        dotnet msbuild $project `
            -restore `
            -t:PublishReleaseSingleFile `
            -p:Configuration=$Configuration `
            -p:Platform=$targetPlatform `
            -p:ReleaseRuntimeMode=$mode `
            -nologo `
            -verbosity:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Release publish failed for $targetPlatform ($mode)."
        }

        $suffix = Get-ArtifactSuffix $mode
        $artifact = Join-Path $repositoryRoot "publish\SETUNA_${Configuration}_${targetPlatform}${suffix}.exe"
        if (-not (Test-Path -LiteralPath $artifact)) {
            throw "Release publish produced no artifact: $artifact"
        }

        $size = [math]::Round((Get-Item -LiteralPath $artifact).Length / 1MB, 1)
        Write-Host "Artifact: $artifact ($size MB)"

        # A framework-dependent WinExe whose shared runtime is missing does not fail with an
        # exit code: its apphost pops a modal "install .NET" dialog and waits, which would hang
        # an unattended run forever. So the runtime is looked for before the process is started,
        # and its absence downgrades to a skip — the artifact itself is fine, this machine just
        # cannot run it.
        if ($mode -eq 'portable') {
            $runtime = Find-DesktopRuntime -TargetPlatform $targetPlatform -Major $requiredRuntimeMajor
            if (-not $runtime) {
                $reason = "$targetPlatform $mode - no $targetPlatform Microsoft.WindowsDesktop.App $requiredRuntimeMajor.x installed"
                Write-Warning "Skipping the self-test: $reason."
                $skipped.Add($reason)
                continue
            }

            Write-Host "Runtime: $runtime"
        }

        $alone = Join-Path ([System.IO.Path]::GetTempPath()) ("SETUNA-alone-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $alone | Out-Null
        try {
            $copied = Join-Path $alone 'SETUNA.exe'
            Copy-Item -LiteralPath $artifact -Destination $copied

            $report = Join-Path $alone 'self-test.txt'
            # --self-test decodes one sample of every supported format, extracts and loads the
            # architecture's libwebp, round-trips the option XML, and checks that nothing sits
            # beside the executable. Results go to a file because a WinExe has no console.
            $process = Start-Process -FilePath $copied -ArgumentList '--self-test', $report -PassThru -Wait
            if (Test-Path -LiteralPath $report) {
                Get-Content -LiteralPath $report | ForEach-Object { Write-Host "  $_" }
            }

            if ($process.ExitCode -ne 0) {
                throw "The standalone $targetPlatform $mode executable failed its self-test (exit $($process.ExitCode))."
            }
        }
        finally {
            Remove-Item -LiteralPath $alone -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($skipped.Count -gt 0) {
    Write-Host ''
    Write-Warning "Published, but not self-tested on this machine:"
    $skipped | ForEach-Object { Write-Warning "  $_" }
}

Write-Host 'Publish validation passed.'
