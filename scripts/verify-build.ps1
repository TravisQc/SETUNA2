[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x86', 'x64')]
    [string]$Platform = 'x64',
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'SETUNA.sln'

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Solution was not found at '$solution'."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required. Install the .NET 8 SDK and re-run.'
}

# One restore covers both platforms: the projects declare RuntimeIdentifiers=win-x86;win-x64,
# so a per-platform restore is not needed and a single-RID one would break the other platform.
if (-not $SkipRestore) {
    Write-Host "Restoring $solution"
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
}

Write-Host "Building $solution ($Configuration|$Platform)"
& dotnet build $solution -c $Configuration -p:Platform=$Platform --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

Write-Host "Testing ($Configuration|$Platform)"
& dotnet test (Join-Path $root 'SETUNATests\SETUNATests.csproj') -c $Configuration -p:Platform=$Platform --no-build
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

$rid = if ($Platform -eq 'x86') { 'win-x86' } else { 'win-x64' }
$tfm = 'net8.0-windows'

Write-Host 'Build gate passed.'
Write-Host ("Application artifact: {0}" -f (Join-Path $root ("SETUNA\bin\{0}\{1}\{2}\{3}\SETUNA.exe" -f $Platform, $Configuration, $tfm, $rid)))
Write-Host ("Test artifact: {0}" -f (Join-Path $root ("SETUNATests\bin\{0}\{1}\{2}\{3}\SETUNATests.dll" -f $Platform, $Configuration, $tfm, $rid)))
