param(
    [string]$OutputPath,
    [string]$PluginMatrixPath = "docs\plugin-positive-function-matrix.json",
    [switch]$SkipBuildAndTests,
    [switch]$AllowDirty,
    [switch]$RequireReleaseEligible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$powerShellHost = (Get-Process -Id $PID).Path
. (Join-Path $PSScriptRoot "release-evidence-io.ps1")
. (Join-Path $PSScriptRoot "automated-acceptance-policy.ps1")

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Get-CommandFailureSummary([object[]]$Output) {
    $summary = (@($Output | Select-Object -Last 20) -join "`n").Trim()
    if ($summary.Length -gt 1500) {
        return $summary.Substring($summary.Length - 1500)
    }
    return $summary
}

function Get-StringSha256([string]$Value) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function New-Evidence(
    [string]$Id,
    [string]$Kind,
    [string]$Path,
    [string]$Sha256) {
    return [ordered]@{
        id = $Id
        kind = $Kind
        path = $Path
        sha256 = $Sha256
    }
}

function New-Gate(
    [string]$Id,
    [string]$Status,
    [string]$Summary,
    [string]$Category,
    [object[]]$Evidence = @(),
    [string]$EnvironmentBlocker = "",
    [string]$NotApplicableReason = "") {
    $gate = [ordered]@{
        id = $Id
        status = $Status
        summary = $Summary
        category = $Category
        evidence = @($Evidence)
    }
    if ($Status -eq "blocked_environment") {
        $gate.environment_blocker = $EnvironmentBlocker
    }
    if ($Status -eq "not_applicable") {
        $gate.not_applicable_reason = $NotApplicableReason
    }
    return $gate
}

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "dotnet CLI was not found."
    }
    $dotnet = $dotnetCommand.Source
}

$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch "^[0-9a-f]{40}$") {
    throw "Unable to resolve the source commit."
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the tracked worktree."
}
$sourceDirty = $trackedStatus.Count -gt 0

$restoreOutput = @()
$buildOutput = @()
$testOutput = @()
$restorePassed = $false
$buildPassed = $false
$testPassed = $false
if (-not $SkipBuildAndTests) {
    $solutionPath = Join-Path $PSScriptRoot "LongBetterWindows.sln"
    $restoreOutput = @(& $dotnet restore $solutionPath --nologo 2>&1)
    $restorePassed = $LASTEXITCODE -eq 0
    if ($restorePassed) {
        $buildOutput = @(& $dotnet build $solutionPath -c Release --no-restore 2>&1)
        $buildPassed = $LASTEXITCODE -eq 0
        if ($buildPassed) {
            $testOutput = @(& $dotnet test `
                (Join-Path $PSScriptRoot `
                    "tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj") `
                -c Release --no-build --no-restore 2>&1)
            $testPassed = $LASTEXITCODE -eq 0
        }
    }
}

$resolvedMatrixPath = Resolve-RepositoryPath $PluginMatrixPath
$matrixOutput = @(& $powerShellHost -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "verify-plugin-positive-matrix.ps1") `
    -MatrixPath $resolvedMatrixPath 2>&1)
$matrixExitCode = $LASTEXITCODE
$matrixJson = $matrixOutput -join "`n"
$matrix = $null
try {
    $matrix = $matrixJson | ConvertFrom-Json
}
catch {
    $matrix = $null
}
$matrixReportSha256 = Get-StringSha256 $matrixJson
$matrixFileSha256 = if (Test-Path -LiteralPath $resolvedMatrixPath -PathType Leaf) {
    (Get-FileHash -LiteralPath $resolvedMatrixPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
} else {
    $null
}

$nativePreflightOutput = @(& $powerShellHost -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "capture-native-performance-evidence.ps1") `
    -PreflightOnly 2>&1)
$nativePreflightExitCode = $LASTEXITCODE
$nativePreflightJson = $nativePreflightOutput -join "`n"
$nativePreflight = $null
try {
    $nativePreflight = $nativePreflightJson | ConvertFrom-Json
}
catch {
    $nativePreflight = $null
}

$lpwp = $null
$lpwpFailure = ""
$lpwpExitCode = $null
$lpwpReportSha256 = $null
$lpwpOutputRoot = $null
if (-not $SkipBuildAndTests) {
    $lpwpOutputRoot = Join-Path ([IO.Path]::GetTempPath()) (
        "long-lpwp-final-" + [Guid]::NewGuid().ToString("N"))
    try {
        $lpwpOutput = @(& $powerShellHost -NoProfile -ExecutionPolicy Bypass `
            -File (Join-Path $PSScriptRoot "verify-lpwp-compatibility.ps1") `
            -OutputDirectory $lpwpOutputRoot -NoBuild 2>&1)
        $lpwpExitCode = $LASTEXITCODE
        $lpwpReportPath = Join-Path $lpwpOutputRoot "lpwp-compatibility-report.json"
        if (Test-Path -LiteralPath $lpwpReportPath -PathType Leaf) {
            $lpwpReportSha256 = (Get-FileHash `
                -LiteralPath $lpwpReportPath -Algorithm SHA256).
                Hash.ToLowerInvariant()
            $lpwp = Get-Content -LiteralPath $lpwpReportPath -Raw -Encoding UTF8 |
                ConvertFrom-Json
        }
        if ($lpwpExitCode -ne 0) {
            $lpwpFailure = Get-CommandFailureSummary $lpwpOutput
        }
    }
    finally {
        if ($null -ne $lpwpOutputRoot -and (Test-Path -LiteralPath $lpwpOutputRoot)) {
            Remove-Item -LiteralPath $lpwpOutputRoot -Recurse -Force
        }
    }
}

$projectPath = Join-Path $PSScriptRoot `
    "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj"
$projectText = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$version = [regex]::Match(
    $projectText,
    "<Version>([^<]+)</Version>").Groups[1].Value
$hostExecutable = Join-Path $PSScriptRoot `
    "src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe"
$hostExists = Test-Path -LiteralPath $hostExecutable -PathType Leaf
$hostSha256 = if ($hostExists) {
    (Get-FileHash -LiteralPath $hostExecutable -Algorithm SHA256).
        Hash.ToLowerInvariant()
} else {
    $null
}
$projectSha256 = (Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).
    Hash.ToLowerInvariant()

$gates = [Collections.Generic.List[object]]::new()
if ($SkipBuildAndTests) {
    $gates.Add((New-Gate "dependency-restore" "not_run" `
        "Dependency restore was skipped by request." "build"))
    $gates.Add((New-Gate "release-build" "not_run" `
        "Release build was skipped by request." "build"))
    $gates.Add((New-Gate "full-automated-tests" "not_run" `
        "Full automated tests were skipped by request." "test"))
} else {
    $restoreEvidence = @(New-Evidence "restore-output" "log" `
        "process://dotnet/restore" `
        (Get-StringSha256 ($restoreOutput -join "`n")))
    $gates.Add((New-Gate "dependency-restore" `
        $(if ($restorePassed) { "passed" } else { "failed" }) `
        $(if ($restorePassed) {
            "Dependency restore completed successfully."
        } else {
            "Dependency restore failed: $(Get-CommandFailureSummary $restoreOutput)"
        }) "build" $restoreEvidence))

    if (-not $restorePassed) {
        $gates.Add((New-Gate "release-build" "not_run" `
            "Release build did not run because dependency restore failed." "build"))
        $gates.Add((New-Gate "full-automated-tests" "not_run" `
            "Full automated tests did not run because dependency restore failed." "test"))
    } else {
        $buildEvidence = @(New-Evidence "build-output" "log" `
            "process://dotnet/build" `
            (Get-StringSha256 ($buildOutput -join "`n")))
        $gates.Add((New-Gate "release-build" `
            $(if ($buildPassed) { "passed" } else { "failed" }) `
            $(if ($buildPassed) {
                "Release build completed successfully."
            } else {
                "Release build failed: $(Get-CommandFailureSummary $buildOutput)"
            }) "build" $buildEvidence))

        if (-not $buildPassed) {
            $gates.Add((New-Gate "full-automated-tests" "not_run" `
                "Full automated tests did not run because Release build failed." "test"))
        } else {
            $testEvidence = @(New-Evidence "test-output" "log" `
                "process://dotnet/test" `
                (Get-StringSha256 ($testOutput -join "`n")))
            $gates.Add((New-Gate "full-automated-tests" `
                $(if ($testPassed) { "passed" } else { "failed" }) `
                $(if ($testPassed) {
                    "Full automated tests completed successfully."
                } else {
                    "Full automated tests failed: $(Get-CommandFailureSummary $testOutput)"
                }) "test" $testEvidence))
        }
    }
}

$matrixReportEvidence = @(New-Evidence "plugin-matrix-report" "json" `
    "process://verify-plugin-positive-matrix" $matrixReportSha256)
$matrixGateItems = if ($null -eq $matrix) { @() } else { @($matrix.gates) }
$matrixClassifiedCount = if ($null -eq $matrix) {
    -1
} else {
    [int]$matrix.passed_gate_count +
        [int]$matrix.failed_gate_count +
        [int]$matrix.environment_blocked_gate_count +
        [int]$matrix.not_run_gate_count +
        [int]$matrix.not_applicable_gate_count
}
$matrixUniqueGateCount = @($matrixGateItems | ForEach-Object {
    [string]$_.id
} | Sort-Object -Unique).Count
$matrixShapeValid = $null -ne $matrix `
    -and [int]$matrix.schema_version -eq 2 `
    -and [string]$matrix.source_commit -eq $sourceCommit `
    -and [bool]$matrix.source_dirty -eq $sourceDirty `
    -and $matrixGateItems.Count -eq [int]$matrix.automated_gate_count `
    -and $matrixClassifiedCount -eq [int]$matrix.automated_gate_count `
    -and $matrixUniqueGateCount -eq $matrixGateItems.Count
$matrixContractPassed = $matrixShapeValid `
    -and $matrixExitCode -eq 0 `
    -and [bool]$matrix.contract_valid
$gates.Add((New-Gate "plugin-matrix-contract" `
    $(if ($matrixContractPassed) { "passed" } else { "failed" }) `
    $(if ($matrixContractPassed) {
        "Plugin matrix schema v2 contract is valid."
    } else {
        "Plugin matrix schema v2 contract is invalid or execution failed."
    }) "plugin_matrix" $matrixReportEvidence))

if ($matrixShapeValid) {
    foreach ($matrixGate in $matrixGateItems) {
        $evidence = @()
        if ([string]$matrixGate.evidence_sha256 -match "^[0-9a-f]{64}$") {
            $evidence = @(New-Evidence "binding" "other" `
                ([string]$matrixGate.evidence_path) `
                ([string]$matrixGate.evidence_sha256))
        } elseif ($null -ne $matrixFileSha256) {
            $evidence = @(New-Evidence "matrix-definition" "json" `
                $resolvedMatrixPath $matrixFileSha256)
        }
        $gates.Add((New-Gate `
            ("plugin-matrix." + [string]$matrixGate.id) `
            ([string]$matrixGate.status) `
            ([string]$matrixGate.summary) `
            "plugin_matrix" `
            $evidence `
            $(if ([string]$matrixGate.status -eq "blocked_environment") {
                "Plugin evidence environment is unavailable."
            } else { "" }) `
            $(if ([string]$matrixGate.status -eq "not_applicable") {
                "Plugin evidence does not apply to this configuration."
            } else { "" })))
    }
}

$hostEvidence = if ($hostExists) {
    @(New-Evidence "release-host" "executable" $hostExecutable $hostSha256)
} else {
    @(New-Evidence "host-project" "other" $projectPath $projectSha256)
}
$gates.Add((New-Gate "release-host-executable" `
    $(if ($hostExists) { "passed" } else { "failed" }) `
    $(if ($hostExists) {
        "Release host executable exists and is bound by SHA-256."
    } else {
        "Release host executable is missing."
    }) "artifact" $hostEvidence))

$nativeEvidence = @(New-Evidence "native-preflight-output" "json" `
    "process://capture-native-performance-evidence/preflight" `
    (Get-StringSha256 $nativePreflightJson))
if ($nativePreflightExitCode -ne 0 -or $null -eq $nativePreflight) {
    $gates.Add((New-Gate "native-performance-preflight" "failed" `
        "Native performance preflight did not return a valid report." `
        "performance" $nativeEvidence))
} elseif ([bool]$nativePreflight.ready) {
    $gates.Add((New-Gate "native-performance-preflight" "passed" `
        "Native performance capture prerequisites are available." `
        "performance" $nativeEvidence))
} else {
    $missing = [Collections.Generic.List[string]]::new()
    if (-not [bool]$nativePreflight.administrator) { $missing.Add("administrator") }
    if (-not [bool]$nativePreflight.wpr_available) { $missing.Add("wpr") }
    if (-not [bool]$nativePreflight.required_profiles_available) {
        $missing.Add("required_profiles")
    }
    if (-not [bool]$nativePreflight.wpa_exporter_available) {
        $missing.Add("wpa_exporter")
    }
    $gates.Add((New-Gate "native-performance-preflight" `
        "blocked_environment" `
        "Native performance capture prerequisites are unavailable." `
        "performance" $nativeEvidence `
        ("Missing prerequisites: " + (@($missing) -join ", "))))
}

$lpwpValid = $null -ne $lpwp `
    -and [string]$lpwp.source_commit -eq $sourceCommit `
    -and [string]$lpwp.protocol -eq "long.plugin.ipc/1.0" `
    -and [bool]$lpwp.dotnet_contract_tests `
    -and [bool]$lpwp.web_sdk_tests `
    -and [bool]$lpwp.runtime_matrix `
    -and [int]$lpwp.fixture_count -eq 6
if ($SkipBuildAndTests) {
    $gates.Add((New-Gate "lpwp-compatibility" "not_run" `
        "LPWP compatibility verification was skipped by request." "lpwp"))
} else {
    $lpwpEvidence = if ($lpwpReportSha256 -match "^[0-9a-f]{64}$") {
        @(New-Evidence "lpwp-report" "json" `
            "process://verify-lpwp-compatibility" $lpwpReportSha256)
    } else {
        @(New-Evidence "lpwp-output" "log" `
            "process://verify-lpwp-compatibility" `
            (Get-StringSha256 $lpwpFailure))
    }
    $gates.Add((New-Gate "lpwp-compatibility" `
        $(if ($lpwpExitCode -eq 0 -and $lpwpValid) { "passed" } else { "failed" }) `
        $(if ($lpwpExitCode -eq 0 -and $lpwpValid) {
            "LPWP compatibility contract completed successfully."
        } else {
            "LPWP compatibility contract failed: $lpwpFailure"
        }) "lpwp" $lpwpEvidence))
}

$gateItems = @($gates)
$gateIds = @($gateItems | ForEach-Object { [string]$_.id })
$uniqueGateCount = @($gateIds | Sort-Object -Unique).Count
$automatedGateCount = $gateItems.Count
$passedGateCount = @($gateItems | Where-Object { $_.status -eq "passed" }).Count
$failedGateCount = @($gateItems | Where-Object { $_.status -eq "failed" }).Count
$environmentBlockedGateCount = @($gateItems | Where-Object {
    $_.status -eq "blocked_environment"
}).Count
$notRunGateCount = @($gateItems | Where-Object { $_.status -eq "not_run" }).Count
$notApplicableGateCount = @($gateItems | Where-Object {
    $_.status -eq "not_applicable"
}).Count
$contractErrors = [Collections.Generic.List[string]]::new()
if (-not $matrixShapeValid) {
    $contractErrors.Add("Plugin matrix report shape or source identity is invalid.")
}
if ($uniqueGateCount -ne $automatedGateCount) {
    $contractErrors.Add("Automated gate IDs must be unique.")
}
$contractValid = $contractErrors.Count -eq 0
$releaseEligible = Get-AutomatedReleaseEligibility `
    -AutomatedGateCount $automatedGateCount `
    -PassedGateCount $passedGateCount `
    -FailedGateCount $failedGateCount `
    -EnvironmentBlockedGateCount $environmentBlockedGateCount `
    -NotRunGateCount $notRunGateCount `
    -NotApplicableGateCount $notApplicableGateCount `
    -ContractValid $contractValid `
    -SourceDirty $sourceDirty

$report = [ordered]@{
    '$schema' = "https://long-assistant.local/schemas/final-closure-report.schema.json"
    schema_version = 2
    generated_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    classification = "final_closure"
    source_commit = $sourceCommit
    source_dirty = $sourceDirty
    allow_dirty = [bool]$AllowDirty
    version = $version
    checks_skipped = [bool]$SkipBuildAndTests
    release_host = [ordered]@{
        exists = $hostExists
        path = $hostExecutable
        sha256 = $hostSha256
    }
    plugin_matrix = if ($null -eq $matrix) {
        $null
    } else {
        [ordered]@{
            schema_version = [int]$matrix.schema_version
            source_commit = [string]$matrix.source_commit
            source_dirty = [bool]$matrix.source_dirty
            plugin_count = [int]$matrix.plugin_count
            command_count = [int]$matrix.command_count
            acceptance_scenario_count = [int]$matrix.acceptance_scenario_count
            automated_gate_count = [int]$matrix.automated_gate_count
            passed_gate_count = [int]$matrix.passed_gate_count
            failed_gate_count = [int]$matrix.failed_gate_count
            environment_blocked_gate_count =
                [int]$matrix.environment_blocked_gate_count
            not_run_gate_count = [int]$matrix.not_run_gate_count
            not_applicable_gate_count = [int]$matrix.not_applicable_gate_count
            contract_valid = [bool]$matrix.contract_valid
            release_eligible = [bool]$matrix.release_eligible
            report_sha256 = $matrixReportSha256
        }
    }
    native_performance_preflight = if ($null -eq $nativePreflight) {
        $null
    } else {
        [ordered]@{
            windows = [bool]$nativePreflight.windows
            administrator = [bool]$nativePreflight.administrator
            wpr_available = [bool]$nativePreflight.wpr_available
            required_profiles_available =
                [bool]$nativePreflight.required_profiles_available
            wpa_exporter_available = [bool]$nativePreflight.wpa_exporter_available
            ready = [bool]$nativePreflight.ready
        }
    }
    lpwp_compatibility = if ($null -eq $lpwp) {
        $null
    } else {
        [ordered]@{
            protocol = [string]$lpwp.protocol
            lpwp_version = [string]$lpwp.lpwp_version
            source_commit = [string]$lpwp.source_commit
            dotnet_contract_tests = [bool]$lpwp.dotnet_contract_tests
            web_sdk_tests = [bool]$lpwp.web_sdk_tests
            runtime_matrix = [bool]$lpwp.runtime_matrix
            fixture_count = [int]$lpwp.fixture_count
            ipc_package_sha256 = [string]$lpwp.ipc_package_sha256
            reference_widget_sha256 = [string]$lpwp.reference_widget_sha256
            report_sha256 = $lpwpReportSha256
            valid = $lpwpValid
        }
    }
    automated_acceptance = [ordered]@{
        automated_gate_count = $automatedGateCount
        passed_gate_count = $passedGateCount
        failed_gate_count = $failedGateCount
        environment_blocked_gate_count = $environmentBlockedGateCount
        not_run_gate_count = $notRunGateCount
        not_applicable_gate_count = $notApplicableGateCount
        contract_valid = $contractValid
        gates = $gateItems
        errors = @($contractErrors)
    }
    release_eligible = $releaseEligible
}
$json = $report | ConvertTo-Json -Depth 12
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = Resolve-RepositoryPath $OutputPath
    $outputDirectory = Split-Path -Parent $resolvedOutput
    if (-not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    [IO.File]::WriteAllText(
        $resolvedOutput,
        $json,
        [Text.UTF8Encoding]::new($false))
}
$json

if (-not $contractValid -or $failedGateCount -gt 0) {
    exit 1
}
if ($RequireReleaseEligible -and -not $releaseEligible) {
    exit 2
}
