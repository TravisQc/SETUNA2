[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

# A context menu's drop-down is created by the OS at the moment it opens, so whether it
# follows the monitor DPI can only be observed in a per-monitor-v2 aware process. The unit
# suite has no manifest and can only drive the arithmetic; this probe borrows SETUNA's
# manifest and opens real drop-downs on every attached monitor.
#
# Exit codes: 0 measured a verdict, 1 ApplyMonitorDpi regressed, 4 only one DPI attached
# (inconclusive, not a failure of the code).
foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\MenuDpiProbe\MenuDpiProbe.csproj'
    dotnet build $project `
        --configuration $Configuration `
        -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "Menu DPI probe build failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\MenuDpiProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\MenuDpiProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "Menu DPI probe output was not generated: $output"
    }

    Write-Host "--- $targetPlatform ---"
    & $output
    $exit = $LASTEXITCODE
    if ($exit -eq 4) {
        Write-Warning "Menu DPI probe was inconclusive for ${targetPlatform}: only one DPI attached."
        continue
    }

    if ($exit -ne 0) {
        throw "Menu DPI probe failed for $targetPlatform (exit $exit)."
    }
}
