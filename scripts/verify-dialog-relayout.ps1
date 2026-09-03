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
    $ScreenshotDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults\dialog-screenshots'
}

# WinForms only scales a control tree on WM_DPICHANGED in a per-monitor-v2 aware process.
# The unit-test host has no manifest, so this check cannot live in the suite: there, fonts
# change and control bounds do not. The probe links SETUNA's manifest instead.
foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\DialogRelayoutProbe\DialogRelayoutProbe.csproj'
    dotnet build $project `
        --configuration $Configuration `
        -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "Dialog relayout probe build failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\DialogRelayoutProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\DialogRelayoutProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Dialog relayout probe output was not generated: $output"
    }

    # A fresh directory per platform, so a stale render from an earlier run cannot be
    # mistaken for this one's evidence.
    $shots = Join-Path $ScreenshotDirectory $targetPlatform
    if (Test-Path -LiteralPath $shots) {
        Remove-Item -LiteralPath $shots -Recurse -Force
    }

    Write-Host "--- $targetPlatform ---"
    & $output $shots
    if ($LASTEXITCODE -ne 0) {
        throw "Dialog relayout probe reported findings for $targetPlatform (exit $LASTEXITCODE)."
    }

    if (Test-Path -LiteralPath $shots) {
        Write-Host ("Screenshots: {0} ({1} files)" -f $shots, (Get-ChildItem -LiteralPath $shots -File).Count)
    }
}
