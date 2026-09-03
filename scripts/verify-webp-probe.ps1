[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x86', 'x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

foreach ($targetPlatform in $Platform) {
    $project = Join-Path $PSScriptRoot '..\probes\WebPProbe\WebPProbe.csproj'
    dotnet restore $project --runtime win-$targetPlatform -p:Platform=$targetPlatform
    if ($LASTEXITCODE -ne 0) {
        throw "WebP probe restore failed for $targetPlatform."
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
        throw "WebP probe publish failed for $targetPlatform."
    }

    $output = Join-Path $PSScriptRoot "..\probes\WebPProbe\bin\$targetPlatform\$Configuration\net8.0-windows10.0.17763.0\win-$targetPlatform\publish\WebPProbe.exe"
    if (-not (Test-Path -LiteralPath $output)) {
        throw "WebP probe output was not generated: $output"
    }

    & $output
    if ($LASTEXITCODE -ne 0) {
        throw "WebP probe failed for $targetPlatform."
    }
}
