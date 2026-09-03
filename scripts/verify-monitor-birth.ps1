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
    $ScreenshotDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults\monitor-birth-screenshots'
}

# The synthetic ladder (verify-dialog-relayout.ps1) posts WM_DPICHANGED to the top-level window.
# A real monitor change also makes the OS send WM_DPICHANGED_BEFOREPARENT to every child window,
# which is where the framework rescales a child's designer-assigned font and the outer rectangle
# of a nested container that scales itself. Only real monitors deliver that, so only this probe
# can tell "the framework does not do it" from "the framework already did it".
#
# Exit 4 means inconclusive: a desktop with a single scale factor cannot fail this check. That is
# reported, not treated as a pass, and not treated as a failure either.
foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\MonitorBirthProbe\MonitorBirthProbe.csproj'
    dotnet build $project `
        --configuration $Configuration `
        -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "Monitor birth probe build failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\MonitorBirthProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\MonitorBirthProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Monitor birth probe output was not generated: $output"
    }

    # A fresh directory per platform, so a stale render from an earlier run cannot be mistaken
    # for this one's evidence.
    $shots = Join-Path $ScreenshotDirectory $targetPlatform
    if (Test-Path -LiteralPath $shots) {
        Remove-Item -LiteralPath $shots -Recurse -Force
    }

    Write-Host "--- $targetPlatform ---"
    & $output $shots
    $probeExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $shots) {
        Write-Host ("Screenshots: {0} ({1} files)" -f $shots, (Get-ChildItem -LiteralPath $shots -File).Count)
    }

    if ($probeExit -eq 4) {
        Write-Warning "Monitor birth probe was inconclusive for ${targetPlatform}: attach a second monitor at a different scale factor for this check to be able to fail."
        continue
    }

    if ($probeExit -ne 0) {
        throw "Monitor birth probe reported findings for $targetPlatform (exit $probeExit)."
    }
}
