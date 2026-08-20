param(
    [string]$PluginId,
    [string]$MatrixPath = "docs/plugin-positive-function-matrix.json",
    [string]$ApprovalDirectory = "docs/plugin-manual-approvals",
    [string]$CandidateDirectory,
    [string]$OutputPath
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

function Get-RepositoryRelativePath([string]$FullPath) {
    $root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $FullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
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

$trackedStatus = @(& git -C $PSScriptRoot status --porcelain `
    --untracked-files=no 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the source worktree."
}
if ($trackedStatus.Count -gt 0) {
    throw "Plugin validation planning requires a clean tracked worktree."
}

$projectPath = Join-Path $PSScriptRoot `
    "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj"
[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$versionNode = $project.SelectSingleNode("/Project/PropertyGroup/Version")
$version = if ($null -eq $versionNode) {
    ""
} else {
    [string]$versionNode.InnerText
}
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "The current product version could not be resolved."
}
if ([string]::IsNullOrWhiteSpace($CandidateDirectory)) {
    $CandidateDirectory = "artifacts/releases/v$version"
}

$candidateRoot = Resolve-RepositoryPath $CandidateDirectory
$releaseRoot = Resolve-RepositoryPath "artifacts/releases"
$releasePrefix = $releaseRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $candidateRoot.StartsWith(
        $releasePrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Plugin validation candidate must be stored under artifacts/releases."
}
$candidatePrefix = $candidateRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$candidateRelativePath = Get-RepositoryRelativePath $candidateRoot
$releaseManifestPath = Join-Path $candidateRoot "release-manifest.json"
if (-not (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf)) {
    throw "Candidate release manifest was not found: $releaseManifestPath"
}
$releaseManifest = Get-Content -LiteralPath $releaseManifestPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$releaseManifestHash = (Get-FileHash -LiteralPath $releaseManifestPath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$candidateCommit = [string]$releaseManifest.commit
if ([int]$releaseManifest.schema_version -ne 1 `
    -or [string]$releaseManifest.version -ne $version `
    -or $candidateCommit -notmatch "^[a-fA-F0-9]{40}$" `
    -or [bool]$releaseManifest.source_dirty `
    -or -not [bool]$releaseManifest.release_eligible) {
    throw "Candidate release manifest is not an eligible clean v$version candidate."
}

$headCommit = (& git -C $PSScriptRoot rev-parse HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the current source commit."
}
& git -C $PSScriptRoot merge-base --is-ancestor $candidateCommit HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Candidate commit is not an ancestor of HEAD: $candidateCommit"
}
$changesAfterCandidate = @(& git -C $PSScriptRoot diff --name-only `
    $candidateCommit HEAD 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to compare HEAD with the candidate commit."
}
$unexpectedChanges = @($changesAfterCandidate | Where-Object {
    $_ -notmatch '^docs/plugin-manual-approvals/[^/]+\.json$'
})
if ($unexpectedChanges.Count -gt 0) {
    throw (
        "Candidate is stale because tracked files other than approval receipts " +
        "changed after it: $($unexpectedChanges -join ', ')")
}

$selfContainedPackages = @($releaseManifest.packages | Where-Object {
    [string]$_.kind -eq "self-contained"
})
if ($selfContainedPackages.Count -ne 1) {
    throw "Candidate manifest must contain exactly one self-contained package."
}
$packageEntry = $selfContainedPackages[0]
$packagePath = [IO.Path]::GetFullPath((Join-Path `
    $candidateRoot ([string]$packageEntry.file)))
if (-not $packagePath.StartsWith(
        $candidatePrefix,
        [StringComparison]::OrdinalIgnoreCase) `
    -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Candidate self-contained package was not found: $packagePath"
}
$packageRelativePath = Get-RepositoryRelativePath $packagePath
$packageHash = (Get-FileHash -LiteralPath $packagePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageHash -ne [string]$packageEntry.sha256) {
    throw "Candidate self-contained package SHA-256 does not match its manifest."
}

$subjectPath = Join-Path $candidateRoot `
    "self-contained\LongBetterWindows.Host.exe"
if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
    throw "Candidate subject executable was not found: $subjectPath"
}
$subjectHash = (Get-FileHash -LiteralPath $subjectPath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $subjectEntries = @($archive.Entries | Where-Object {
        $_.FullName.Replace("\", "/") -match '(^|/)LongBetterWindows\.Host\.exe$'
    })
    if ($subjectEntries.Count -ne 1) {
        throw "Candidate ZIP must contain exactly one LongBetterWindows.Host.exe."
    }
    $entryStream = $subjectEntries[0].Open()
    try {
        $archivedSubjectHash = Get-StreamSha256 $entryStream
    }
    finally {
        $entryStream.Dispose()
    }
}
finally {
    $archive.Dispose()
}
if ($archivedSubjectHash -ne $subjectHash) {
    throw "Unpacked candidate EXE does not match the EXE stored in the verified ZIP."
}

$matrixFile = Resolve-RepositoryPath $MatrixPath
$approvalRoot = Resolve-RepositoryPath $ApprovalDirectory
if (-not (Test-Path -LiteralPath $matrixFile -PathType Leaf)) {
    throw "Plugin positive matrix was not found: $matrixFile"
}
$matrix = Get-Content -LiteralPath $matrixFile -Raw -Encoding UTF8 |
    ConvertFrom-Json

$verificationLines = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot "verify-plugin-positive-matrix.ps1") `
    -MatrixPath $matrixFile 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Plugin matrix verification failed: $($verificationLines -join ' ')"
}
$matrixVerification = ($verificationLines -join [Environment]::NewLine) |
    ConvertFrom-Json
if (-not [bool]$matrixVerification.contract_valid) {
    throw "Plugin matrix contract is invalid."
}

$approvedKeys = @{}
$staleApprovalReceipts = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $approvalRoot -PathType Container) {
    foreach ($receiptFile in Get-ChildItem -LiteralPath $approvalRoot `
            -Filter "*.json" -File) {
        $receipt = Get-Content -LiteralPath $receiptFile.FullName `
            -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$receipt.schema_version -ne 2 `
            -or [string]$receipt.candidate_version -ne $version `
            -or [string]$receipt.candidate_commit -ne $candidateCommit `
            -or [string]$receipt.candidate_directory -ne $candidateRelativePath `
            -or [string]$receipt.release_manifest_sha256 -ne $releaseManifestHash `
            -or [string]$receipt.self_contained_package -ne $packageRelativePath `
            -or [string]$receipt.self_contained_package_sha256 -ne $packageHash `
            -or [string]$receipt.subject_executable_sha256 -ne $subjectHash) {
            $staleApprovalReceipts.Add(
                (Get-RepositoryRelativePath $receiptFile.FullName))
            continue
        }
        $approvedKeys["$([string]$receipt.plugin_id)/$([string]$receipt.manual_check_id)"] = $true
    }
}

$riskOrder = @{ low = 0; medium = 1; high = 2; critical = 3 }
$pending = [System.Collections.Generic.List[object]]::new()
$pluginIndex = 0
foreach ($plugin in @($matrix.plugins)) {
    $manualIndex = 0
    foreach ($check in @($plugin.acceptance_scenarios)) {
        $key = "$([string]$plugin.id)/$([string]$check.id)"
        if ([bool]$check.required_for_release `
            -and -not $approvedKeys.ContainsKey($key)) {
            $risk = [string]$plugin.risk
            if (-not $riskOrder.ContainsKey($risk)) {
                throw "Unknown plugin risk level: $risk"
            }
            $pending.Add([PSCustomObject]@{
                Plugin = $plugin
                Check = $check
                RiskRank = $riskOrder[$risk]
                PluginIndex = $pluginIndex
                ManualIndex = $manualIndex
            })
        }
        $manualIndex++
    }
    $pluginIndex++
}
$orderedPending = @($pending | Sort-Object RiskRank, PluginIndex, ManualIndex)
if (-not [string]::IsNullOrWhiteSpace($PluginId)) {
    $knownPlugin = @($matrix.plugins | Where-Object {
        [string]$_.id -eq $PluginId
    })
    if ($knownPlugin.Count -ne 1) {
        throw "Plugin matrix entry was not found or is ambiguous: $PluginId"
    }
    $orderedPending = @($orderedPending | Where-Object {
        [string]$_.Plugin.id -eq $PluginId
    })
}

$next = $null
if ($orderedPending.Count -gt 0) {
    $item = $orderedPending[0]
    $safeName = ("$([string]$item.Plugin.id)--$([string]$item.Check.id)") `
        -replace "[^A-Za-z0-9._-]", "_"
    $shortCommit = $candidateCommit.Substring(0, 7)
    $evidenceDirectory = "artifacts/quality/plugin-manual-$shortCommit/$safeName"
    $subjectRelativePath = Get-RepositoryRelativePath $subjectPath
    $next = [ordered]@{
        plugin_id = [string]$item.Plugin.id
        manual_check_id = [string]$item.Check.id
        risk = [string]$item.Plugin.risk
        runtime = [string]$item.Plugin.runtime
        commands = @($item.Check.commands | ForEach-Object { [string]$_ })
        description = [string]$item.Check.description
        evidence_directory = $evidenceDirectory
        subject_executable = $subjectRelativePath
        subject_executable_sha256 = $subjectHash
        approval_command = (
            ".\approve-plugin-manual-evidence.ps1 " +
            "-PluginId '$([string]$item.Plugin.id)' " +
            "-ManualCheckId '$([string]$item.Check.id)' " +
            "-Reviewer '<reviewer>' -Notes '<observed result>' " +
            "-EvidenceFiles @('<evidence-file-1>','<evidence-file-2>') " +
            "-CandidateDirectory '$candidateRelativePath' " +
            "-SubjectExecutable '$subjectRelativePath' -ConfirmPassed")
    }
}

$plan = [ordered]@{
    schema_version = 1
    classification = "plugin_manual_validation_plan"
    generated_at = [DateTimeOffset]::Now.ToString("o")
    version = $version
    candidate_commit = $candidateCommit
    head_commit = $headCommit
    candidate_directory = $candidateRelativePath
    release_manifest_sha256 = $releaseManifestHash
    self_contained_package = $packageRelativePath
    self_contained_package_sha256 = $packageHash
    subject_executable_sha256 = $subjectHash
    required_manual_check_count = [int]$matrixVerification.required_manual_check_count
    approval_receipt_count = $approvedKeys.Count
    stale_approval_receipt_count = $staleApprovalReceipts.Count
    stale_approval_receipts = @($staleApprovalReceipts)
    pending_manual_check_count = $pending.Count
    selected_plugin_id = if ([string]::IsNullOrWhiteSpace($PluginId)) {
        $null
    } else {
        $PluginId
    }
    selected_scope_complete = $orderedPending.Count -eq 0
    complete = $pending.Count -eq 0
    next = $next
}

$json = $plan | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $planPath = Resolve-RepositoryPath $OutputPath
    Write-NewJsonFileAtomically `
        -Value $plan `
        -Path $planPath `
        -Depth 8 `
        -Label "Plugin manual validation plan"
    Write-Host "Validation plan created: $planPath"
}
$json
