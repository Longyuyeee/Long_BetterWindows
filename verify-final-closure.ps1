param(
    [string]$OutputPath,
    [switch]$SkipBuildAndTests,
    [switch]$AllowDirty,
    [switch]$RequireHumanValidationReady
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Get-CommandFailureSummary([object[]]$Output) {
    return (@($Output | Select-Object -Last 20) -join "`n").Trim()
}

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "dotnet CLI was not found."
    }
    $dotnet = $dotnetCommand.Source
}

$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Unable to resolve the source commit."
}
$trackedStatus = @(& git -C $PSScriptRoot status `
    --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the tracked worktree."
}
$sourceDirty = $trackedStatus.Count -gt 0

$buildPassed = $true
$testPassed = $true
$buildFailure = ""
$testFailure = ""
if (-not $SkipBuildAndTests) {
    $buildOutput = @(& $dotnet build `
        (Join-Path $PSScriptRoot "LongBetterWindows.sln") `
        -c Release --no-restore 2>&1)
    $buildPassed = $LASTEXITCODE -eq 0
    if (-not $buildPassed) {
        $buildFailure = Get-CommandFailureSummary $buildOutput
    } else {
        $testOutput = @(& $dotnet test `
            (Join-Path $PSScriptRoot `
                "tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj") `
            -c Release --no-build --no-restore 2>&1)
        $testPassed = $LASTEXITCODE -eq 0
        if (-not $testPassed) {
            $testFailure = Get-CommandFailureSummary $testOutput
        }
    }
}

$matrixOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "verify-plugin-positive-matrix.ps1") `
    2>&1)
$matrixExitCode = $LASTEXITCODE
$matrix = $null
if ($matrixExitCode -eq 0) {
    $matrix = ($matrixOutput -join "`n") | ConvertFrom-Json
}

$nativePreflightOutput = @(& powershell.exe -NoProfile `
    -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot `
        "capture-native-performance-evidence.ps1") `
    -PreflightOnly 2>&1)
$nativePreflightExitCode = $LASTEXITCODE
$nativePreflight = $null
if ($nativePreflightExitCode -eq 0) {
    $nativePreflight = (
        $nativePreflightOutput -join "`n") | ConvertFrom-Json
}

$lpwp = $null
$lpwpFailure = ""
$lpwpExitCode = $null
$lpwpOutputRoot = $null
if (-not $SkipBuildAndTests) {
    $lpwpOutputRoot = Join-Path ([IO.Path]::GetTempPath()) (
        "long-lpwp-final-" + [Guid]::NewGuid().ToString("N"))
    try {
        $lpwpOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File (Join-Path $PSScriptRoot "verify-lpwp-compatibility.ps1") `
            -OutputDirectory $lpwpOutputRoot -NoBuild 2>&1)
        $lpwpExitCode = $LASTEXITCODE
        $lpwpReportPath = Join-Path $lpwpOutputRoot "lpwp-compatibility-report.json"
        if ($lpwpExitCode -eq 0 -and (Test-Path -LiteralPath $lpwpReportPath -PathType Leaf)) {
            $lpwp = Get-Content -LiteralPath $lpwpReportPath -Raw -Encoding UTF8 |
                ConvertFrom-Json
        } else {
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

$machineBlockers = [Collections.Generic.List[string]]::new()
if ($SkipBuildAndTests) {
    $machineBlockers.Add("Release build and full tests were skipped.")
    $machineBlockers.Add("LPWP compatibility verification was skipped.")
}
if (-not $buildPassed) {
    $machineBlockers.Add("Release build failed.")
}
if (-not $testPassed) {
    $machineBlockers.Add("Full automated tests failed.")
}
if ($matrixExitCode -ne 0 -or $null -eq $matrix `
    -or -not [bool]$matrix.contract_valid) {
    $machineBlockers.Add("Plugin positive-function contract is invalid.")
}
$lpwpValid = $null -ne $lpwp `
    -and [string]$lpwp.source_commit -eq $sourceCommit.ToLowerInvariant() `
    -and [string]$lpwp.protocol -eq "long.plugin.ipc/1.0" `
    -and [bool]$lpwp.dotnet_contract_tests `
    -and [bool]$lpwp.web_sdk_tests `
    -and [bool]$lpwp.runtime_matrix `
    -and [int]$lpwp.fixture_count -eq 6
if (-not $SkipBuildAndTests -and ($lpwpExitCode -ne 0 -or -not $lpwpValid)) {
    $machineBlockers.Add("LPWP compatibility contract is invalid.")
}
if (-not $hostExists) {
    $machineBlockers.Add("Release host executable is missing.")
}
if ($sourceDirty -and -not $AllowDirty) {
    $machineBlockers.Add("Tracked worktree is dirty.")
}

$pluginApprovalStatus = if (
    $null -ne $matrix `
    -and [int]$matrix.pending_or_blocked_manual_count -eq 0 `
    -and [int]$matrix.failed_manual_count -eq 0) {
    "passed"
} else {
    "pending"
}
$nativeStatus = if (
    $null -ne $nativePreflight `
    -and [bool]$nativePreflight.ready) {
    "pending_capture_and_analysis"
} else {
    "blocked_requires_elevated_session"
}
$humanValidation = @(
    [ordered]@{
        id = "plugin-positive-functions"
        status = $pluginApprovalStatus
        scope = "25 plugin checks covering 42 commands"
        guide_id = "plugin-manual-evidence"
    },
    [ordered]@{
        id = "taskbar-visual-grouping"
        status = "pending"
        scope = "Explorer grouping, icon appearance, pin and unpin"
        guide_id = "final-closure-handoff"
    },
    [ordered]@{
        id = "physical-dpi"
        status = "pending"
        scope = "100%, 125%, 150% and 200% light/dark matrix"
        guide_id = "physical-dpi-release"
    },
    [ordered]@{
        id = "physical-accessibility"
        status = "pending"
        scope = "high contrast, reduced motion and Narrator/NVDA"
        guide_id = "accessibility-release"
    },
    [ordered]@{
        id = "native-performance"
        status = $nativeStatus
        scope = "elevated WPR CPU and DesktopComposition analysis"
        guide_id = "native-wpr-performance"
    },
    [ordered]@{
        id = "clean-windows-and-download"
        status = "pending"
        scope = "SmartScreen, antivirus, install, upgrade and uninstall"
        guide_id = "final-closure-handoff"
    },
    [ordered]@{
        id = "production-marketplace-rehearsal"
        status = "blocked_requires_controlled_credentials"
        scope = "real HTTPS Registry/CDN deploy and rollback"
        guide_id = "marketplace-registry-release"
    },
    [ordered]@{
        id = "lpwp-long-grid-e2e"
        status = "blocked_requires_long_grid_repository"
        scope = "Long Grid handshake, catalog, command cancellation and plugin.open cross-repository E2E"
        guide_id = "lpwp-integration"
    },
    [ordered]@{
        id = "lpwp-widget-desktop"
        status = "pending"
        scope = "Reference Widget install, dual instance, move, resize, restart restore, pause and help"
        guide_id = "user-final-validation"
    },
    [ordered]@{
        id = "lpwp-signed-reference"
        status = "blocked_requires_publisher_identity"
        scope = "Approved Marketplace publisher key, signed reference bundle and independent fingerprint review"
        guide_id = "lpwp-signed-reference-release"
    }
)

$readyForHumanValidation = $machineBlockers.Count -eq 0
$remainingHumanCount = @($humanValidation | Where-Object {
    [string]$_.status -ne "passed"
}).Count
$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::Now.ToString("O")
    classification = "final_closure_readiness"
    source_commit = $sourceCommit.ToLowerInvariant()
    source_dirty = $sourceDirty
    allow_dirty = [bool]$AllowDirty
    version = $version
    checks_skipped = [bool]$SkipBuildAndTests
    release_build_passed = if ($SkipBuildAndTests) {
        $null
    } else {
        $buildPassed
    }
    full_tests_passed = if ($SkipBuildAndTests) {
        $null
    } else {
        $testPassed
    }
    build_failure = $buildFailure
    test_failure = $testFailure
    release_host = [ordered]@{
        exists = $hostExists
        sha256 = $hostSha256
    }
    plugin_matrix = if ($null -eq $matrix) {
        $null
    } else {
        [ordered]@{
            contract_valid = [bool]$matrix.contract_valid
            plugin_count = [int]$matrix.plugin_count
            command_count = [int]$matrix.command_count
            automated_evidence_count =
                [int]$matrix.automated_evidence_count
            approval_receipt_count =
                [int]$matrix.approval_receipt_count
            pending_or_blocked_manual_count =
                [int]$matrix.pending_or_blocked_manual_count
            failed_manual_count =
                [int]$matrix.failed_manual_count
        }
    }
    native_performance_preflight = $nativePreflight
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
            valid = $lpwpValid
        }
    }
    lpwp_failure = $lpwpFailure
    machine_blockers = @($machineBlockers)
    ready_for_human_validation = $readyForHumanValidation
    remaining_human_validation_count = $remainingHumanCount
    human_validation = $humanValidation
    release_eligible = $readyForHumanValidation `
        -and $remainingHumanCount -eq 0
}
$json = $report | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = Resolve-RepositoryPath $OutputPath
    $outputDirectory = Split-Path -Parent $resolvedOutput
    if (-not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force |
            Out-Null
    }
    [IO.File]::WriteAllText(
        $resolvedOutput,
        $json,
        [Text.UTF8Encoding]::new($false))
}
$json

if ($RequireHumanValidationReady -and -not $readyForHumanValidation) {
    exit 2
}
