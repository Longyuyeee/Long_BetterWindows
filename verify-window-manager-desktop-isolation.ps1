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
$testGroups = @(
    [ordered]@{
        id = "layout"
        filter = "FullyQualifiedName~WindowManagerDesktopIsolationTests.Layout_UsesExactMonitorWorkAreaOnDisposableWindow"
        expected_case_count = 9
    },
    [ordered]@{
        id = "topmost"
        filter = "FullyQualifiedName=LongBetterWindows.Tests.WindowManagerDesktopIsolationTests.Topmost_RoundTripPreservesDisposableWindowGeometry"
        expected_case_count = 1
    }
)

try {
    $caseResults = @()
    $groupResults = @()
    $testExitCode = 0
    foreach ($group in $testGroups) {
        $trxName = "$($group.id).trx"
        $trxPath = Join-Path $runRoot $trxName
        $testOutput = @(& $DotnetPath test $projectPath `
            -c Release `
            --no-build `
            --filter $group.filter `
            --logger "trx;LogFileName=$trxName" `
            --results-directory $runRoot 2>&1)
        $groupExitCode = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
            throw "WindowManager $($group.id) tests produced no TRX report. Exit code: $groupExitCode`n$($testOutput -join "`n")"
        }

        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw -Encoding UTF8
        $groupCases = @($trx.TestRun.Results.UnitTestResult |
            ForEach-Object {
                [PSCustomObject]@{
                    name = [string]$_.testName
                    outcome = ([string]$_.outcome).ToLowerInvariant()
                    duration = [string]$_.duration
                }
            } | Sort-Object name)
        $groupPassedCount = @($groupCases |
            Where-Object { $_.outcome -eq "passed" }).Count
        $groupPassed = $groupExitCode -eq 0 -and
            $groupCases.Count -eq $group.expected_case_count -and
            $groupPassedCount -eq $group.expected_case_count
        if (-not $groupPassed) {
            $testExitCode = 1
        }
        $caseResults += $groupCases
        $groupResults += [ordered]@{
            id = $group.id
            expected_case_count = $group.expected_case_count
            executed_case_count = $groupCases.Count
            passed_case_count = $groupPassedCount
            test_exit_code = $groupExitCode
            passed = $groupPassed
        }
    }

    $caseResults = @($caseResults | Sort-Object name)
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
        visible_window_created = $true
        foreground_activation_attempted = $false
        pointer_or_keyboard_input_generated = $false
        test_process_count = $testGroups.Count
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
        test_groups = $groupResults
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
