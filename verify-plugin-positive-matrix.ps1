param(
    [string]$MatrixPath = "docs/plugin-positive-function-matrix.json",
    [string]$SourceRoot = "src",
    [string]$OutputPath,
    [switch]$RequireReleaseEligible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "release-evidence-io.ps1")
function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Add-MatrixError(
    [System.Collections.Generic.List[string]]$Errors,
    [string]$Message) {
    $Errors.Add($Message)
}

$matrixFile = Resolve-RepositoryPath $MatrixPath
$sourceDirectory = Resolve-RepositoryPath $SourceRoot
if (-not (Test-Path -LiteralPath $matrixFile -PathType Leaf)) {
    throw "Plugin positive matrix was not found: $matrixFile"
}
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "Plugin source root was not found: $sourceDirectory"
}

$matrix = Get-Content -LiteralPath $matrixFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()
$manifestById = @{}
$manifestFiles = Get-ChildItem -LiteralPath $sourceDirectory -Directory |
    ForEach-Object {
        $candidate = Join-Path $_.FullName "manifest.json"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Get-Item -LiteralPath $candidate
        }
    }
foreach ($manifestFile in $manifestFiles) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not ([string]$manifest.id).StartsWith(
            "com.long.",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    if ($manifestById.ContainsKey([string]$manifest.id)) {
        Add-MatrixError $errors `
            "Duplicate source plugin id: $($manifest.id)"
        continue
    }
    $manifestById[[string]$manifest.id] = [PSCustomObject]@{
        Path = $manifestFile.FullName
        Manifest = $manifest
    }
}

$matrixById = @{}
$automatedEvidenceCount = 0
$manualPendingCount = 0
$manualFailedCount = 0
$manualRequiredCount = 0
$matrixCommandCount = 0
foreach ($plugin in @($matrix.plugins)) {
    $pluginId = [string]$plugin.id
    if ($matrixById.ContainsKey($pluginId)) {
        Add-MatrixError $errors "Duplicate matrix plugin id: $pluginId"
        continue
    }
    $matrixById[$pluginId] = $plugin
    if (-not $manifestById.ContainsKey($pluginId)) {
        Add-MatrixError $errors `
            "Matrix plugin has no source manifest: $pluginId"
        continue
    }

    $manifest = $manifestById[$pluginId].Manifest
    $sourceCommands = @($manifest.commands |
        ForEach-Object { [string]$_.id } |
        Sort-Object)
    $matrixCommands = @($plugin.commands |
        ForEach-Object { [string]$_ } |
        Sort-Object)
    $matrixCommandCount += $matrixCommands.Count
    if (($sourceCommands -join "`n") -ne ($matrixCommands -join "`n")) {
        Add-MatrixError $errors `
            "Command coverage mismatch for $pluginId. Source=[$($sourceCommands -join ', ')] Matrix=[$($matrixCommands -join ', ')]"
    }

    $expectedRuntime = if (
        [System.IO.Path]::GetExtension([string]$manifest.entry_point) -ieq
        ".dll") {
        "native"
    } else {
        "web"
    }
    if ([string]$plugin.runtime -ne $expectedRuntime) {
        Add-MatrixError $errors `
            "Runtime mismatch for $pluginId. Expected $expectedRuntime."
    }

    $evidenceItems = @($plugin.automated_evidence)
    if ($evidenceItems.Count -eq 0) {
        Add-MatrixError $errors `
            "Plugin has no automated evidence binding: $pluginId"
    }
    foreach ($evidence in $evidenceItems) {
        $automatedEvidenceCount++
        $evidenceFile = Resolve-RepositoryPath ([string]$evidence.path)
        if (-not (Test-Path -LiteralPath $evidenceFile -PathType Leaf)) {
            Add-MatrixError $errors `
                "Evidence file is missing for ${pluginId}: $($evidence.path)"
            continue
        }
        $symbol = [string]$evidence.symbol
        if ([string]::IsNullOrWhiteSpace($symbol)) {
            Add-MatrixError $errors `
                "Evidence symbol is empty for ${pluginId}: $($evidence.path)"
            continue
        }
        if (-not (Select-String -LiteralPath $evidenceFile `
                -SimpleMatch $symbol -Quiet)) {
            Add-MatrixError $errors `
                "Evidence symbol '$symbol' was not found for $pluginId in $($evidence.path)"
        }
    }

    $acceptanceScenarios = @($plugin.acceptance_scenarios)
    if ($acceptanceScenarios.Count -eq 0) {
        Add-MatrixError $errors `
            "Plugin has no explicit acceptance scenario: $pluginId"
    }
    $manualIds = @{}
    $manualCommandCoverage = @{}
    foreach ($manualCheck in $acceptanceScenarios) {
        $manualId = [string]$manualCheck.id
        if ($manualIds.ContainsKey($manualId)) {
            Add-MatrixError $errors `
                "Duplicate acceptance scenario id for ${pluginId}: $manualId"
        }
        $manualIds[$manualId] = $true
        foreach ($commandId in @($manualCheck.commands)) {
            $commandText = [string]$commandId
            if ($commandText -notin $matrixCommands) {
                Add-MatrixError $errors `
                    "Acceptance scenario references unknown command: ${pluginId}/${manualId}/$commandText"
            }
            $manualCommandCoverage[$commandText] = $true
        }
        if ([bool]$manualCheck.required_for_release) {
            $manualRequiredCount++
            $manualPendingCount++
        }
    }
    foreach ($commandId in $matrixCommands) {
        if (-not $manualCommandCoverage.ContainsKey($commandId)) {
            Add-MatrixError $errors `
                "Command has no acceptance scenario: ${pluginId}/$commandId"
        }
    }
}

foreach ($pluginId in $manifestById.Keys) {
    if (-not $matrixById.ContainsKey($pluginId)) {
        Add-MatrixError $errors `
            "Source plugin is absent from matrix: $pluginId"
    }
}

$expectedPluginCount = [int]$matrix.policy.required_plugin_count
$expectedCommandCount = [int]$matrix.policy.required_command_count
if ($manifestById.Count -ne $expectedPluginCount) {
    Add-MatrixError $errors `
        "Source plugin count is $($manifestById.Count), expected $expectedPluginCount."
}
if ($matrixById.Count -ne $expectedPluginCount) {
    Add-MatrixError $errors `
        "Matrix plugin count is $($matrixById.Count), expected $expectedPluginCount."
}
if ($matrixCommandCount -ne $expectedCommandCount) {
    Add-MatrixError $errors `
        "Matrix command count is $matrixCommandCount, expected $expectedCommandCount."
}

$matrixSha256 = (Get-FileHash -LiteralPath $matrixFile `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the source commit."
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain `
    --untracked-files=no 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the source worktree."
}
$sourceDirty = $trackedStatus.Count -gt 0
$releaseEligible = $errors.Count -eq 0 -and
    $manualPendingCount -eq 0 -and
    $manualFailedCount -eq 0 -and
    -not $sourceDirty
$report = [ordered]@{
    schema_version = 1
    classification = "plugin_positive_matrix"
    generated_at = [DateTimeOffset]::Now.ToString("o")
    matrix_path = $matrixFile
    matrix_sha256 = $matrixSha256
    source_commit = ([string]$sourceCommit).Trim()
    source_dirty = $sourceDirty
    plugin_count = $matrixById.Count
    command_count = $matrixCommandCount
    automated_evidence_count = $automatedEvidenceCount
    required_manual_check_count = $manualRequiredCount
    approval_receipt_count = 0
    stale_approval_receipt_count = 0
    pending_or_blocked_manual_count = $manualPendingCount
    failed_manual_count = $manualFailedCount
    contract_valid = $errors.Count -eq 0
    release_eligible = $releaseEligible
    errors = @($errors)
}
$json = $report | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportPath = Resolve-RepositoryPath $OutputPath
    $reportDirectory = Split-Path -Parent $reportPath
    if (-not (Test-Path -LiteralPath $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force |
            Out-Null
    }
    [System.IO.File]::WriteAllText(
        $reportPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}
$json

if ($errors.Count -gt 0) {
    exit 1
}
if ($RequireReleaseEligible -and -not $releaseEligible) {
    exit 2
}
