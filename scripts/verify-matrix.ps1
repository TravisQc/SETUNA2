[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string[]] $Configurations = @('Debug', 'Release'),
    [ValidateSet('x86', 'x64')]
    [string[]] $Platforms = @('x86', 'x64'),
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

# English tool output, so the warning inventory reads the same on every machine and the
# test counters below cannot be defeated by a localized summary line.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'SETUNA.sln'
$testProject = Join-Path $root 'SETUNATests\SETUNATests.csproj'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'TestResults\build-matrix'
}

# A fresh directory per run: a stale log from a configuration outside this run would be
# read as part of this run's inventory.
if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

# One restore covers both platforms: the projects declare RuntimeIdentifiers=win-x86;win-x64.
Write-Host "Restoring $solution"
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

$rows = @()
$failures = @()

# Deliberately not $configuration/$platform: PowerShell variable names are
# case-insensitive, so a loop variable of that name overwrites the parameter it is
# iterating, and the second pass of the outer loop would see a single-element array.
foreach ($cfg in $Configurations) {
    foreach ($plat in $Platforms) {
        $label = "$cfg-$plat"
        Write-Host "--- $label ---"

        # --no-incremental is what makes the inventory mean anything: an up-to-date
        # project is skipped and reports no warnings at all, so an incremental matrix
        # records zero for whichever configuration was built last.
        $warningLog = Join-Path $OutputDirectory "warnings-$label.txt"
        & dotnet build $solution `
            -c $cfg `
            -p:Platform=$plat `
            --no-restore `
            --no-incremental `
            -nologo `
            -verbosity:minimal `
            "-flp:LogFile=$warningLog;WarningsOnly;Verbosity=normal" | Out-Null
        $buildExit = $LASTEXITCODE

        $trx = Join-Path $OutputDirectory "tests-$label.trx"
        $testExit = 0
        if ($buildExit -eq 0) {
            & dotnet test $testProject `
                -c $cfg `
                -p:Platform=$plat `
                --no-build `
                --nologo `
                --logger "trx;LogFileName=$trx" | Out-Null
            $testExit = $LASTEXITCODE
        }
        else {
            $failures += "${label}: build failed (exit $buildExit)"
        }

        if ($buildExit -eq 0 -and $testExit -ne 0) {
            $failures += "${label}: tests failed (exit $testExit)"
        }

        $codes = @()
        if (Test-Path -LiteralPath $warningLog) {
            $codes = @(Select-String -LiteralPath $warningLog -Pattern '\bwarning ([A-Za-z]+[0-9]+)' -AllMatches |
                ForEach-Object { $_.Matches } |
                ForEach-Object { $_.Groups[1].Value })
        }

        $inventory = 'none'
        $grouped = $codes | Group-Object | Sort-Object Count -Descending
        if ($grouped) {
            $inventory = (($grouped | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', ')
        }

        # The TRX counters instead of the console summary: locale-independent, and they
        # distinguish "no tests ran" from "all tests passed".
        $tests = 'no trx produced'
        if (Test-Path -LiteralPath $trx) {
            $counters = ([xml](Get-Content -LiteralPath $trx -Raw)).TestRun.ResultSummary.Counters
            $tests = "$($counters.passed)/$($counters.total) passed, $($counters.failed) failed"
            if ([int]$counters.total -eq 0) {
                $failures += "${label}: the test run discovered no tests"
            }
        }

        $rows += [pscustomobject]@{
            Configuration = $cfg
            Platform      = $plat
            BuildExit     = $buildExit
            TestExit      = $testExit
            Warnings      = $codes.Count
            Inventory     = $inventory
            Tests         = $tests
        }

        Write-Host "  build exit $buildExit, test exit $testExit, $tests, $($codes.Count) warning(s): $inventory"
    }
}

$summaryPath = Join-Path $OutputDirectory 'summary.md'
$lines = @(
    "# Build matrix - $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    '',
    '| Configuration | Platform | Build | Tests | Warnings | Inventory |',
    '| --- | --- | --- | --- | --- | --- |'
)
foreach ($row in $rows) {
    $lines += "| $($row.Configuration) | $($row.Platform) | exit $($row.BuildExit) | $($row.Tests) | $($row.Warnings) | $($row.Inventory) |"
}
$lines += @(
    '',
    'Per-configuration warning text is in `warnings-<configuration>-<platform>.txt`; the full',
    'test results are in `tests-<configuration>-<platform>.trx`.'
)
$lines | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host "Warning inventory and per-configuration logs: $OutputDirectory"
Write-Host "Summary: $summaryPath"

if ($failures.Count -gt 0) {
    throw ('The build matrix failed:' + [Environment]::NewLine + ($failures -join [Environment]::NewLine))
}

Write-Host 'Build matrix passed.'
