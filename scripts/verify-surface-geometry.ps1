[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $ScreenshotDirectory
)

$ErrorActionPreference = 'Stop'

if (-not $ScreenshotDirectory) {
    $ScreenshotDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults\surface-screenshots'
}

# Physical-pixel geometry only means anything in a per-monitor-v2 aware process: an unaware
# one is told 96 DPI and handed virtualised screen bounds, so every rectangle agrees with
# every other by construction and nothing is proved. The probe borrows SETUNA's manifest.
foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\SurfaceGeometryProbe\SurfaceGeometryProbe.csproj'
    dotnet build $project `
        --configuration $Configuration `
        -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "Surface geometry probe build failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\SurfaceGeometryProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\SurfaceGeometryProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Surface geometry probe output was not generated: $output"
    }

    # A fresh directory per platform, so a stale render cannot be mistaken for this run's.
    $shots = Join-Path $ScreenshotDirectory $targetPlatform
    if (Test-Path -LiteralPath $shots) {
        Remove-Item -LiteralPath $shots -Recurse -Force
    }

    Write-Host "--- $targetPlatform ---"
    & $output $shots
    if ($LASTEXITCODE -ne 0) {
        throw "Surface geometry probe reported findings for $targetPlatform (exit $LASTEXITCODE)."
    }

    if (Test-Path -LiteralPath $shots) {
        Write-Host ("Screenshots: {0} ({1} files)" -f $shots, (Get-ChildItem -LiteralPath $shots -File).Count)
    }
}
