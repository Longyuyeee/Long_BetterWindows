param(
    [string]$TestProject = "tests/LongBetterWindows.Tests/LongBetterWindows.Tests.csproj",
    [string]$OutputPath,
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [int]$ExpectedCaseCount = 10
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

$projectPath = Resolve-RepositoryPath $TestProject
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "WindowManager test project was not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw "dotnet executable was not found: $DotnetPath"
}

$temporaryBase = [System.IO.Path]::GetFullPath((Join-Path (
    [System.IO.Path]::GetTempPath()) "LongAssistant-WindowManager-TestResults"))
New-Item -ItemType Directory -Path $temporaryBase -Force | Out-Null
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryBase (
    [Guid]::NewGuid().ToString("N"))))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$trxPath = Join-Path $runRoot "window-manager-desktop.trx"

try {
    $testOutput = @(& $DotnetPath test $projectPath `
        -c Release `
        --no-build `
        --filter "FullyQualifiedName~WindowManagerDesktopIsolationTests" `
        --logger "trx;LogFileName=window-manager-desktop.trx" `
        --results-directory $runRoot 2>&1)
    $testExitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "WindowManager isolation tests produced no TRX report. Exit code: $testExitCode`n$($testOutput -join "`n")"
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw -Encoding UTF8
    $results = @($trx.TestRun.Results.UnitTestResult)
    $caseResults = @($results | ForEach-Object {
        [PSCustomObject]@{
            name = [string]$_.testName
            outcome = ([string]$_.outcome).ToLowerInvariant()
            duration = [string]$_.duration
        }
    } | Sort-Object name)
    $passedCount = @($caseResults |
        Where-Object { $_.outcome -eq "passed" }).Count
    $failedCases = @($caseResults |
        Where-Object { $_.outcome -ne "passed" })
    $passed = $testExitCode -eq 0 -and
        $caseResults.Count -eq $ExpectedCaseCount -and
        $passedCount -eq $ExpectedCaseCount

    $testAssembly = Resolve-RepositoryPath (
        "tests/LongBetterWindows.Tests/bin/Release/net8.0-windows/LongBetterWindows.Tests.dll")
    $commit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
    $trackedStatus = ((& git -C $PSScriptRoot status `
        --porcelain --untracked-files=no) -join "`n")
    $evidence = [ordered]@{
        schema_version = 1
        recorded_at = [DateTimeOffset]::UtcNow.ToString("O")
        source_commit = $commit
        source_dirty = -not [string]::IsNullOrWhiteSpace($trackedStatus)
        isolated = $true
        disposable_native_window_only = $true
        visible_window_created = $false
        foreground_activation_attempted = $false
        pointer_or_keyboard_input_generated = $false
        test_assembly_sha256 = if (Test-Path -LiteralPath $testAssembly) {
            (Get-FileHash -LiteralPath $testAssembly -Algorithm SHA256).
                Hash.ToLowerInvariant()
        } else { $null }
        expected_case_count = $ExpectedCaseCount
        executed_case_count = $caseResults.Count
        passed_case_count = $passedCount
        failed_case_count = $failedCases.Count
        test_exit_code = $testExitCode
        passed = $passed
        cases = $caseResults
    }
}
finally {
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    if ($resolvedRunRoot.StartsWith(
            $temporaryBase + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRunRoot)) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/window-manager-desktop-isolation-$stamp/report.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
New-Item -ItemType Directory -Path (
    [System.IO.Path]::GetDirectoryName($outputFile)) -Force | Out-Null
$evidence | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $outputFile -Encoding UTF8

Write-Host "WindowManager desktop isolation: $($evidence.passed_case_count)/$ExpectedCaseCount cases passed"
Write-Host "Evidence: $outputFile"
if (-not $evidence.passed) {
    $failedCases | ForEach-Object {
        Write-Error "$($_.name): $($_.outcome)"
    }
    exit 1
}
