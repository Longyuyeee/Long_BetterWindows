param(
    [string]$MatrixPath = "docs/plugin-positive-function-matrix.json",
    [string]$SourceRoot = "src",
    [string]$ApprovalDirectory = "docs/plugin-manual-approvals",
    [string]$OutputPath,
    [switch]$RequireReleaseEligible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

function Test-ApprovalReceipt(
    [System.IO.FileInfo]$ReceiptFile,
    [string]$PluginId,
    [string]$ManualCheckId,
    [string]$ManifestPath,
    [string[]]$ExpectedCommands,
    [System.Collections.Generic.List[string]]$Errors) {
    try {
        $receipt = Get-Content -LiteralPath $ReceiptFile.FullName `
            -Raw -Encoding UTF8 | ConvertFrom-Json
        $label = "$PluginId/$ManualCheckId"
        if ([int]$receipt.schema_version -ne 1 `
            -or [string]$receipt.plugin_id -ne $PluginId `
            -or [string]$receipt.manual_check_id -ne $ManualCheckId `
            -or [string]$receipt.status -ne "passed") {
            Add-MatrixError $Errors "Manual approval receipt identity/status is invalid: $label"
            return $false
        }
        if ([string]::IsNullOrWhiteSpace([string]$receipt.reviewer) `
            -or [string]::IsNullOrWhiteSpace([string]$receipt.notes)) {
            Add-MatrixError $Errors "Manual approval receipt lacks reviewer/notes: $label"
            return $false
        }
        $sourceCommit = [string]$receipt.source_commit
        if ($sourceCommit -notmatch "^[a-fA-F0-9]{40}$") {
            Add-MatrixError $Errors "Manual approval source commit is invalid: $label"
            return $false
        }
        & git -C $PSScriptRoot merge-base --is-ancestor `
            $sourceCommit HEAD 2>$null
        if ($LASTEXITCODE -ne 0) {
            Add-MatrixError $Errors "Manual approval source commit is not an ancestor: $label"
            return $false
        }
        $sourceChanges = @(& git -C $PSScriptRoot diff `
            --name-only $sourceCommit HEAD -- src 2>$null)
        if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -gt 0) {
            Add-MatrixError $Errors "Product source changed after manual approval: $label"
            return $false
        }
        $manifestHash = (Get-FileHash -LiteralPath $ManifestPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([string]$receipt.manifest_sha256 -ne $manifestHash) {
            Add-MatrixError $Errors "Manual approval manifest hash mismatch: $label"
            return $false
        }
        $receiptCommands = @($receipt.commands |
            ForEach-Object { [string]$_ } | Sort-Object)
        if (($receiptCommands -join "`n") -ne `
            (($ExpectedCommands | Sort-Object) -join "`n")) {
            Add-MatrixError $Errors "Manual approval command set mismatch: $label"
            return $false
        }
        if ([string]$receipt.subject_executable_sha256 `
                -notmatch "^[a-fA-F0-9]{64}$") {
            Add-MatrixError $Errors "Manual approval subject hash is invalid: $label"
            return $false
        }
        $evidenceFiles = @($receipt.evidence_files)
        if ($evidenceFiles.Count -eq 0) {
            Add-MatrixError $Errors "Manual approval has no evidence file hashes: $label"
            return $false
        }
        foreach ($evidence in $evidenceFiles) {
            if ([string]$evidence.relative_path `
                    -notlike "artifacts/quality/*" `
                -or [string]$evidence.sha256 `
                    -notmatch "^[a-fA-F0-9]{64}$" `
                -or [long]$evidence.size_bytes -le 0) {
                Add-MatrixError $Errors "Manual approval evidence metadata is invalid: $label"
                return $false
            }
        }
        return $true
    }
    catch {
        Add-MatrixError $Errors (
            "Manual approval receipt could not be read: " +
            "$PluginId/$ManualCheckId ($($_.Exception.Message))")
        return $false
    }
}

$matrixFile = Resolve-RepositoryPath $MatrixPath
$sourceDirectory = Resolve-RepositoryPath $SourceRoot
$approvalRoot = Resolve-RepositoryPath $ApprovalDirectory
if (-not (Test-Path -LiteralPath $matrixFile -PathType Leaf)) {
    throw "Plugin positive matrix was not found: $matrixFile"
}
if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "Plugin source root was not found: $sourceDirectory"
}

$matrix = Get-Content -LiteralPath $matrixFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()
$approvalByKey = @{}
if (Test-Path -LiteralPath $approvalRoot -PathType Container) {
    foreach ($receiptFile in Get-ChildItem -LiteralPath $approvalRoot `
            -Filter "*.json" -File) {
        try {
            $receipt = Get-Content -LiteralPath $receiptFile.FullName `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            $key = "$([string]$receipt.plugin_id)/$([string]$receipt.manual_check_id)"
            if ($approvalByKey.ContainsKey($key)) {
                Add-MatrixError $errors "Duplicate manual approval receipt: $key"
            } else {
                $approvalByKey[$key] = $receiptFile
            }
        }
        catch {
            Add-MatrixError $errors (
                "Manual approval receipt could not be indexed: " +
                "$($receiptFile.Name) ($($_.Exception.Message))")
        }
    }
}
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
$manualApprovalReceiptCount = 0
$consumedApprovalKeys = @{}
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

    $manualChecks = @($plugin.manual_checks)
    if ($manualChecks.Count -eq 0) {
        Add-MatrixError $errors `
            "Plugin has no explicit positive manual check: $pluginId"
    }
    $manualIds = @{}
    $manualCommandCoverage = @{}
    foreach ($manualCheck in $manualChecks) {
        $manualId = [string]$manualCheck.id
        if ($manualIds.ContainsKey($manualId)) {
            Add-MatrixError $errors `
                "Duplicate manual check id for ${pluginId}: $manualId"
        }
        $manualIds[$manualId] = $true
        foreach ($commandId in @($manualCheck.commands)) {
            $commandText = [string]$commandId
            if ($commandText -notin $matrixCommands) {
                Add-MatrixError $errors `
                    "Manual check references unknown command: ${pluginId}/${manualId}/$commandText"
            }
            $manualCommandCoverage[$commandText] = $true
        }
        $status = [string]$manualCheck.status
        $approvalKey = "$pluginId/$manualId"
        $approvedByReceipt = $false
        if ($approvalByKey.ContainsKey($approvalKey)) {
            $consumedApprovalKeys[$approvalKey] = $true
            $receiptValid = Test-ApprovalReceipt `
                $approvalByKey[$approvalKey] `
                $pluginId `
                $manualId `
                $manifestById[$pluginId].Path `
                @($manualCheck.commands | ForEach-Object { [string]$_ }) `
                $errors
            if ($receiptValid) {
                $status = "passed"
                $approvedByReceipt = $true
                $manualApprovalReceiptCount++
            }
        }
        if ($status -notin @("pending", "passed", "failed", "blocked")) {
            Add-MatrixError $errors `
                "Invalid manual status for ${pluginId}/${manualId}: $status"
        }
        if ([bool]$manualCheck.required_for_release) {
            $manualRequiredCount++
            if ($status -eq "pending" -or $status -eq "blocked") {
                $manualPendingCount++
            } elseif ($status -eq "failed") {
                $manualFailedCount++
            }
        }
        if ($status -eq "passed" -and -not $approvedByReceipt) {
            $evidencePath = [string]$manualCheck.evidence_path
            $evidenceSha256 = [string]$manualCheck.evidence_sha256
            if ([string]::IsNullOrWhiteSpace($evidencePath) -or
                $evidenceSha256 -notmatch '^[a-fA-F0-9]{64}$') {
                Add-MatrixError $errors `
                    "Passed manual check lacks evidence path/SHA-256: ${pluginId}/${manualId}"
            } else {
                $manualEvidence = Resolve-RepositoryPath $evidencePath
                if (-not (Test-Path -LiteralPath $manualEvidence `
                        -PathType Leaf)) {
                    Add-MatrixError $errors `
                        "Manual evidence file is missing: ${pluginId}/${manualId}"
                } else {
                    $actualSha256 = (Get-FileHash -LiteralPath `
                        $manualEvidence -Algorithm SHA256).Hash
                    if ($actualSha256 -ne $evidenceSha256) {
                        Add-MatrixError $errors `
                            "Manual evidence hash mismatch: ${pluginId}/${manualId}"
                    }
                }
            }
        }
    }
    foreach ($commandId in $matrixCommands) {
        if (-not $manualCommandCoverage.ContainsKey($commandId)) {
            Add-MatrixError $errors `
                "Command has no positive manual check: ${pluginId}/$commandId"
        }
    }
}

foreach ($approvalKey in $approvalByKey.Keys) {
    if (-not $consumedApprovalKeys.ContainsKey($approvalKey)) {
        Add-MatrixError $errors `
            "Manual approval receipt has no matching matrix check: $approvalKey"
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
    generated_at = [DateTimeOffset]::Now.ToString("o")
    matrix_path = $matrixFile
    matrix_sha256 = $matrixSha256
    source_commit = ([string]$sourceCommit).Trim()
    source_dirty = $sourceDirty
    plugin_count = $matrixById.Count
    command_count = $matrixCommandCount
    automated_evidence_count = $automatedEvidenceCount
    required_manual_check_count = $manualRequiredCount
    approval_receipt_count = $manualApprovalReceiptCount
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
