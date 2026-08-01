param(
    [string]$SessionPath,
    [Parameter(Mandatory = $true)]
    [string]$Reviewer,
    [Parameter(Mandatory = $true)]
    [string]$Notes,
    [Parameter(Mandatory = $true)]
    [string[]]$EvidenceFiles,
    [switch]$ConfirmPassed,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

if (-not $ConfirmPassed) {
    throw (
        "Completing a plugin validation requires -ConfirmPassed after the " +
        "reviewer inspects every evidence file.")
}
if ([string]::IsNullOrWhiteSpace($Reviewer) `
    -or [string]::IsNullOrWhiteSpace($Notes)) {
    throw "Reviewer and Notes are required."
}

$plannerOutput = @(& (Join-Path $PSScriptRoot `
    "plan-next-plugin-validation.ps1"))
$plan = ($plannerOutput -join [Environment]::NewLine) | ConvertFrom-Json
if ($null -eq $plan.next) {
    throw "There is no pending plugin manual check to complete."
}
if ([string]::IsNullOrWhiteSpace($SessionPath)) {
    $SessionPath = Join-Path ([string]$plan.next.evidence_directory) `
        "validation-session.json"
}
$sessionFile = Resolve-RepositoryPath $SessionPath
if (-not (Test-Path -LiteralPath $sessionFile -PathType Leaf)) {
    throw "Plugin validation session was not found: $sessionFile"
}
$session = Get-Content -LiteralPath $sessionFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ([int]$session.schema_version -ne 1 `
    -or [string]$session.classification -ne `
        "plugin_manual_validation_session" `
    -or [string]$session.candidate_version -ne [string]$plan.version `
    -or [string]$session.candidate_commit -ne `
        [string]$plan.candidate_commit `
    -or [string]$session.plugin_id -ne [string]$plan.next.plugin_id `
    -or [string]$session.manual_check_id -ne `
        [string]$plan.next.manual_check_id `
    -or [string]$session.subject_executable -ne `
        [string]$plan.next.subject_executable `
    -or [string]$session.subject_executable_sha256 -ne `
        [string]$plan.next.subject_executable_sha256 `
    -or [string]$session.launch_status -ne "started" `
    -or [string]$session.review_status -ne `
        "pending_human_observation") {
    throw "Plugin validation session does not match the current pending candidate check."
}

$sessionDirectory = Split-Path -Parent $sessionFile
$sessionPrefix = $sessionDirectory.TrimEnd(
    [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$reviewEvidence = @($EvidenceFiles | ForEach-Object {
    $evidencePath = Resolve-RepositoryPath $_
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "Plugin validation evidence was not found: $evidencePath"
    }
    if (-not $evidencePath.StartsWith(
            $sessionPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence must belong to the selected validation session: $evidencePath"
    }
    if ($evidencePath.Equals(
            $sessionFile,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The validation session file cannot replace observed UI evidence."
    }
    if ((Get-Item -LiteralPath $evidencePath).Length -le 0) {
        throw "Plugin validation evidence is empty: $evidencePath"
    }
    $evidencePath
})
if ($reviewEvidence.Count -eq 0) {
    throw "At least one observed UI evidence file is required."
}

$approvalParameters = @{
    PluginId = [string]$session.plugin_id
    ManualCheckId = [string]$session.manual_check_id
    Reviewer = $Reviewer.Trim()
    Notes = $Notes.Trim()
    EvidenceFiles = @($reviewEvidence) + @($sessionFile)
    SubjectExecutable = [string]$session.subject_executable
    ConfirmPassed = $true
}
if ($Replace) {
    $approvalParameters.Replace = $true
}
& (Join-Path $PSScriptRoot "approve-plugin-manual-evidence.ps1") `
    @approvalParameters

$safeName = ("$([string]$session.plugin_id)--" +
    [string]$session.manual_check_id) -replace "[^A-Za-z0-9._-]", "_"
$receiptPath = Join-Path $PSScriptRoot `
    "docs\plugin-manual-approvals\$safeName.json"
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw "Approval command completed without creating its receipt."
}
$receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
[ordered]@{
    schema_version = 1
    classification = "plugin_manual_validation_completion"
    status = "receipt_created_pending_commit"
    plugin_id = [string]$receipt.plugin_id
    manual_check_id = [string]$receipt.manual_check_id
    reviewer = [string]$receipt.reviewer
    candidate_commit = [string]$session.candidate_commit
    subject_executable_sha256 = [string]$receipt.subject_executable_sha256
    evidence_file_count = @($receipt.evidence_files).Count
    receipt_path = $receiptPath
} | ConvertTo-Json -Depth 5
