[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x86', 'x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# The probe links SETUNA\app.manifest, so it observes the DPI awareness SETUNA.exe
# gets. The unit-test suite cannot: its host process has no application manifest.
foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\DpiAwarenessProbe\DpiAwarenessProbe.csproj'
    dotnet restore $project --runtime win-$targetPlatform -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "DPI awareness probe restore failed for $targetPlatform."
    }

    dotnet publish $project `
        --configuration $Configuration `
        --framework net8.0-windows10.0.17763.0 `
        --runtime win-$targetPlatform `
        --self-contained true `
        --no-restore `
        -p:Platform=$targetPlatform `
        -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        throw "DPI awareness probe publish failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\DpiAwarenessProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\publish\DpiAwarenessProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "DPI awareness probe output was not generated: $output"
    }

    Write-Host "--- $targetPlatform ---"
    & $output
    if ($LASTEXITCODE -ne 0) {
        throw "DPI awareness probe reported a non-PerMonitorV2 process for $targetPlatform."
    }
}
