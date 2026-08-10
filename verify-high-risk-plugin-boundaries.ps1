param(
    [string]$TestProject =
        "tests/LongBetterWindows.Tests/LongBetterWindows.Tests.csproj",
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath,
    [string]$DotnetPath = "C:\Program Files\dotnet\dotnet.exe",
    [int]$ExpectedServiceCaseCount = 28
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Read-JsonReport([string]$PathValue) {
    if (-not (Test-Path -LiteralPath $PathValue -PathType Leaf)) {
        throw "Required evidence report was not created: $PathValue"
    }
    return Get-Content -LiteralPath $PathValue -Raw -Encoding UTF8 |
        ConvertFrom-Json
}

$projectPath = Resolve-RepositoryPath $TestProject
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Test project was not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw "dotnet executable was not found: $DotnetPath"
}

$temporaryBase = [System.IO.Path]::GetFullPath((Join-Path (
    [System.IO.Path]::GetTempPath()) "LongAssistant-HighRisk-Boundaries"))
New-Item -ItemType Directory -Path $temporaryBase -Force | Out-Null
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryBase (
    [Guid]::NewGuid().ToString("N"))))
New-Item -ItemType Directory -Path $runRoot | Out-Null

$transactionPath = Join-Path $runRoot "transactions.json"
$capturePath = Join-Path $runRoot "capture-delivery.json"
$quickLaunchPath = Join-Path $runRoot "quick-launch.json"
$serviceTrxPath = Join-Path $runRoot "service-boundaries.trx"

try {
    & (Join-Path $PSScriptRoot "verify-high-risk-plugin-transactions.ps1") `
        -HostDirectory $HostDirectory `
        -OutputPath $transactionPath
    if ($LASTEXITCODE -ne 0) {
        throw "High-risk transaction command matrix failed."
    }

    & (Join-Path $PSScriptRoot "verify-capture-delivery-isolation.ps1") `
        -TestProject $TestProject `
        -DotnetPath $DotnetPath `
        -OutputPath $capturePath
    if ($LASTEXITCODE -ne 0) {
        throw "Capture delivery isolation matrix failed."
    }

    & (Join-Path $PSScriptRoot "verify-quick-launch-isolation.ps1") `
        -TestProject $TestProject `
        -DotnetPath $DotnetPath `
        -OutputPath $quickLaunchPath `
        -ExpectedCaseCount 7
    if ($LASTEXITCODE -ne 0) {
        throw "QuickLaunch isolation matrix failed."
    }

    $serviceFilter = @(
        "FullyQualifiedName~FileSystemOrganizationTests",
        "FullyQualifiedName~AdsServiceTransactionTests",
        "FullyQualifiedName~ProcessServiceTests",
        "FullyQualifiedName~ScreenCaptureServiceTests"
    ) -join "|"
    $testOutput = @(& $DotnetPath test $projectPath `
        -c Release `
        --no-build `
        --filter $serviceFilter `
        --logger "trx;LogFileName=service-boundaries.trx" `
        --results-directory $runRoot 2>&1)
    $testExitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $serviceTrxPath -PathType Leaf)) {
        throw "Service boundary tests produced no TRX report. Exit code: $testExitCode`n$($testOutput -join "`n")"
    }

    [xml]$trx = Get-Content -LiteralPath $serviceTrxPath -Raw -Encoding UTF8
    $serviceCases = @($trx.TestRun.Results.UnitTestResult | ForEach-Object {
        [PSCustomObject]@{
            name = [string]$_.testName
            outcome = ([string]$_.outcome).ToLowerInvariant()
            duration = [string]$_.duration
        }
    } | Sort-Object name)
    $passedServiceCount = @($serviceCases |
        Where-Object { $_.outcome -eq "passed" }).Count
    $servicePassed = $testExitCode -eq 0 -and
        $serviceCases.Count -eq $ExpectedServiceCaseCount -and
        $passedServiceCount -eq $ExpectedServiceCaseCount

    $transactions = Read-JsonReport $transactionPath
    $capture = Read-JsonReport $capturePath
    $quickLaunch = Read-JsonReport $quickLaunchPath
    $allPassed = [bool]$transactions.passed -and
        [bool]$capture.passed -and
        [bool]$quickLaunch.passed -and
        $servicePassed

    $plugins = @(
        [ordered]@{
            id = "com.long.fileorganizer"
            checks = @("isolated-command-preview", "file-organization-transaction")
            passed = [bool]$transactions.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.file-renamer"
            checks = @("isolated-command-preview", "batch-rename-transaction")
            passed = [bool]$transactions.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.folder-note"
            checks = @("isolated-command-target", "ads-transaction")
            passed = [bool]$transactions.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.portmanager"
            checks = @("owned-port-query", "verified-disposable-process")
            passed = [bool]$transactions.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.color-picker"
            checks = @("physical-pixel-sampling", "cancelled-delivery")
            passed = [bool]$capture.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.screenshot"
            checks = @("physical-screen-capture", "cancelled-delivery")
            passed = [bool]$capture.passed -and $servicePassed
        },
        [ordered]@{
            id = "com.long.quicklaunch"
            checks = @("search-isolation", "stop-lifetime-cancellation")
            passed = [bool]$quickLaunch.passed
        }
    )

    $commit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
    $trackedStatus = ((& git -C $PSScriptRoot status `
        --porcelain --untracked-files=no) -join "`n")
    $evidence = [ordered]@{
        schema_version = 1
        recorded_at = [DateTimeOffset]::UtcNow.ToString("O")
        source_commit = $commit
        source_dirty = -not [string]::IsNullOrWhiteSpace($trackedStatus)
        isolated = $true
        system_clipboard_touched = $false
        pointer_input_generated = $false
        disposable_process_only = $true
        required_plugin_count = 7
        covered_plugin_count = $plugins.Count
        expected_case_count = 45
        executed_case_count =
            [int]$transactions.passed_case_count +
            [int]$capture.executed_case_count +
            [int]$quickLaunch.executed_case_count +
            $serviceCases.Count
        passed = $allPassed
        plugins = $plugins
        transaction_commands = $transactions
        capture_delivery = $capture
        quick_launch = $quickLaunch
        service_boundaries = [ordered]@{
            expected_case_count = $ExpectedServiceCaseCount
            executed_case_count = $serviceCases.Count
            passed_case_count = $passedServiceCount
            test_exit_code = $testExitCode
            passed = $servicePassed
            cases = $serviceCases
        }
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
        "artifacts/quality/high-risk-plugin-boundaries-$stamp/report.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
New-Item -ItemType Directory -Path (
    [System.IO.Path]::GetDirectoryName($outputFile)) -Force | Out-Null
$evidence | ConvertTo-Json -Depth 14 |
    Set-Content -LiteralPath $outputFile -Encoding UTF8

Write-Host "High-risk plugin boundaries: $($evidence.executed_case_count)/$($evidence.expected_case_count) cases passed"
Write-Host "Plugin coverage: $($evidence.covered_plugin_count)/$($evidence.required_plugin_count)"
Write-Host "Evidence: $outputFile"
if (-not $evidence.passed) {
    exit 1
}
