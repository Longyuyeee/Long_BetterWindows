param(
    [string]$MatrixPath = "docs/plugin-positive-function-matrix.json",
    [string]$SourceRoot = "src",
    [string]$ApprovalDirectory = "docs/plugin-manual-approvals",
    [string]$OutputPath,
    [switch]$RequireReleaseEligible
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "release-evidence-io.ps1")
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script:candidateBindingCache = @{}

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

function Get-RepositoryRelativePath([string]$FullPath) {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $FullPath.StartsWith(
            $root,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $FullPath"
    }
    return $FullPath.Substring($root.Length).Replace("\", "/")
}

function Get-StreamSha256([System.IO.Stream]$Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($Stream))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-CandidateBinding([string]$CandidateDirectory) {
    if ($script:candidateBindingCache.ContainsKey($CandidateDirectory)) {
        return $script:candidateBindingCache[$CandidateDirectory]
    }
    $binding = $null
    try {
        $candidateRoot = Resolve-RepositoryPath $CandidateDirectory
        $releaseRoot = Resolve-RepositoryPath "artifacts/releases"
        $releasePrefix = $releaseRoot.TrimEnd(
            [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $candidateRoot.StartsWith(
                $releasePrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Candidate directory is outside artifacts/releases."
        }
        $candidatePrefix = $candidateRoot.TrimEnd(
            [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $manifestPath = Join-Path $candidateRoot "release-manifest.json"
        $subjectPath = Join-Path $candidateRoot `
            "self-contained\LongBetterWindows.Host.exe"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
            -or -not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
            throw "Candidate manifest or subject executable is missing."
        }
        $manifest = Get-Content -LiteralPath $manifestPath `
            -Raw -Encoding UTF8 | ConvertFrom-Json
        $packages = @($manifest.packages | Where-Object {
            [string]$_.kind -eq "self-contained"
        })
        if ([int]$manifest.schema_version -ne 1 `
            -or [bool]$manifest.source_dirty `
            -or -not [bool]$manifest.release_eligible `
            -or $packages.Count -ne 1) {
            throw "Candidate release contract is invalid."
        }
        $packagePath = [IO.Path]::GetFullPath((Join-Path `
            $candidateRoot ([string]$packages[0].file)))
        if (-not $packagePath.StartsWith(
                $candidatePrefix,
                [StringComparison]::OrdinalIgnoreCase) `
            -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            throw "Candidate package is missing or outside its directory."
        }
        $packageHash = (Get-FileHash -LiteralPath $packagePath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($packageHash -ne [string]$packages[0].sha256) {
            throw "Candidate package does not match its manifest."
        }
        $subjectHash = (Get-FileHash -LiteralPath $subjectPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            $entries = @($archive.Entries | Where-Object {
                $_.FullName.Replace("\", "/") -match `
                    '(^|/)LongBetterWindows\.Host\.exe$'
            })
            if ($entries.Count -ne 1) {
                throw "Candidate ZIP subject identity is ambiguous."
            }
            $stream = $entries[0].Open()
            try {
                $archiveSubjectHash = Get-StreamSha256 $stream
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
        if ($archiveSubjectHash -ne $subjectHash) {
            throw "Candidate extracted subject differs from its ZIP."
        }
        $binding = [PSCustomObject]@{
            CandidateCommit = ([string]$manifest.commit).ToLowerInvariant()
            CandidateVersion = [string]$manifest.version
            CandidateDirectory = Get-RepositoryRelativePath $candidateRoot
            ReleaseManifestSha256 = (Get-FileHash -LiteralPath `
                $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
            SelfContainedPackage = $packagePath
            SelfContainedPackageSha256 = $packageHash
            SubjectExecutableSha256 = $subjectHash
        }
    }
    catch {
        $binding = $null
    }
    $script:candidateBindingCache[$CandidateDirectory] = $binding
    return $binding
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
        if ([int]$receipt.schema_version -ne 2) {
            $script:staleApprovalReceiptCount++
            return $false
        }
        if ([string]$receipt.plugin_id -ne $PluginId `
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
            $script:staleApprovalReceiptCount++
            return $false
        }
        $candidateDirectory = [string]$receipt.candidate_directory
        $candidateCommit = [string]$receipt.candidate_commit
        $candidateBinding = Get-CandidateBinding $candidateDirectory
        $receiptPackagePath = $null
        try {
            $receiptPackagePath = Resolve-RepositoryPath `
                ([string]$receipt.self_contained_package)
        }
        catch {}
        if ($null -eq $candidateBinding `
            -or $null -eq $receiptPackagePath `
            -or $candidateCommit -notmatch "^[a-fA-F0-9]{40}$" `
            -or $candidateBinding.CandidateCommit -ne $candidateCommit `
            -or $candidateBinding.CandidateDirectory -ne `
                $candidateDirectory.Replace("\", "/") `
            -or $candidateBinding.CandidateVersion -ne `
                [string]$receipt.candidate_version `
            -or $candidateBinding.ReleaseManifestSha256 -ne `
                [string]$receipt.release_manifest_sha256 `
            -or $candidateBinding.SelfContainedPackageSha256 -ne `
                [string]$receipt.self_contained_package_sha256 `
            -or $candidateBinding.SelfContainedPackage -ne $receiptPackagePath `
            -or $candidateBinding.SubjectExecutableSha256 -ne `
                [string]$receipt.subject_executable_sha256) {
            $script:staleApprovalReceiptCount++
            return $false
        }
        & git -C $PSScriptRoot merge-base --is-ancestor `
            $candidateCommit HEAD 2>$null
        $candidateChanges = if ($LASTEXITCODE -eq 0) {
            @(& git -C $PSScriptRoot diff --name-only `
                $candidateCommit HEAD 2>$null | Where-Object {
                    $_ -notmatch '^docs/plugin-manual-approvals/[^/]+\.json$'
                })
        } else {
            @("candidate-not-ancestor")
        }
        if ($LASTEXITCODE -ne 0 -or $candidateChanges.Count -gt 0) {
            $script:staleApprovalReceiptCount++
            return $false
        }
        $manifestHash = Get-NormalizedTextSha256 $ManifestPath
        if ([string]$receipt.manifest_hash_format -ne "utf8-lf-v1" `
            -or [string]$receipt.manifest_sha256 -ne $manifestHash) {
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
$staleApprovalReceiptCount = 0
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
        $status = "pending"
        $approvalKey = "$pluginId/$manualId"
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
                $manualApprovalReceiptCount++
            }
        }
        if ([bool]$manualCheck.required_for_release) {
            $manualRequiredCount++
            if ($status -eq "pending" -or $status -eq "blocked") {
                $manualPendingCount++
            } elseif ($status -eq "failed") {
                $manualFailedCount++
            }
        }
    }
    foreach ($commandId in $matrixCommands) {
        if (-not $manualCommandCoverage.ContainsKey($commandId)) {
            Add-MatrixError $errors `
                "Command has no acceptance scenario: ${pluginId}/$commandId"
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
    approval_receipt_count = $manualApprovalReceiptCount
    stale_approval_receipt_count = $staleApprovalReceiptCount
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
