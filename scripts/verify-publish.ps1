[CmdletBinding()]
param(
    [ValidateSet('x86', 'x64')]
    [string[]] $Platform = @('x86', 'x64'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repositoryRoot 'SETUNA\SETUNA.csproj'

# The publish artifact is the deliverable, so the checks below run against a copy of it
# alone in an empty directory — the situation a user is in after downloading one file.
# Running them in `publish\` would pass on companion files that happen to be siblings.
foreach ($targetPlatform in $Platform) {
    Write-Host "--- $Configuration $targetPlatform ---"

    # -restore in the same invocation, per platform, on purpose. A publish evaluates with
    # SelfContained/PublishSingleFile and the platform's RuntimeIdentifier as global
    # properties, and a restore triggered under those can leave project.assets.json carrying
    # only that RID — after which the other platform fails NETSDK1047. Restoring immediately
    # before each publish, with the properties that publish will use, makes the order of the
    # two irrelevant.
    dotnet msbuild $project `
        -restore `
        -t:PublishReleaseSingleFile `
        -p:Configuration=$Configuration `
        -p:Platform=$targetPlatform `
        -nologo `
        -verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Release publish failed for $targetPlatform."
    }

    $artifact = Join-Path $repositoryRoot "publish\SETUNA_${Configuration}_${targetPlatform}.exe"
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "Release publish produced no artifact: $artifact"
    }

    $size = [math]::Round((Get-Item -LiteralPath $artifact).Length / 1MB, 1)
    Write-Host "Artifact: $artifact ($size MB)"

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
            throw "The standalone $targetPlatform executable failed its self-test (exit $($process.ExitCode))."
        }
    }
    finally {
        Remove-Item -LiteralPath $alone -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host 'Publish validation passed.'
