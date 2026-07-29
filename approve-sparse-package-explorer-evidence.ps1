#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $ExpectedCertificateThumbprint,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $ReviewNotes,
    [Parameter(Mandatory=$true)] [switch] $ConfirmSelectionPrimaryMenu,
    [Parameter(Mandatory=$true)] [switch] $ConfirmBackgroundPrimaryMenu,
    [Parameter(Mandatory=$true)] [switch] $ConfirmCorrectNoteTarget,
    [Parameter(Mandatory=$true)] [switch] $ConfirmExplorerStable,
    [Parameter(Mandatory=$true)] [switch] $ConfirmUninstallRemovedMenu
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
$expectedThumbprint = $ExpectedCertificateThumbprint.Replace(' ', '').ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
if ($expectedThumbprint -notmatch '^[0-9a-f]{40,128}$') {
    throw 'ExpectedCertificateThumbprint is invalid.'
}
if ([string]::IsNullOrWhiteSpace($Reviewer)) { throw 'Reviewer must not be empty.' }
if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 12) {
    throw 'ReviewNotes must contain at least 12 characters.'
}
$required = @(
    $ConfirmSelectionPrimaryMenu,
    $ConfirmBackgroundPrimaryMenu,
    $ConfirmCorrectNoteTarget,
    $ConfirmExplorerStable,
    $ConfirmUninstallRemovedMenu
)
if ($required -contains $false) {
    throw 'Every Sparse Package Explorer review confirmation is required.'
}

$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$evidencePath = Join-Path $root 'sparse-package-explorer-evidence.json'
if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
    throw "Sparse Package Explorer evidence was not found: $evidencePath"
}
$evidenceHashBeforeReview = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).
    Hash.ToLowerInvariant()
$evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($evidence.classification -ne 'sparse_package_explorer_evidence') {
    throw 'Unexpected Sparse Package Explorer evidence classification.'
}
if ([string]$evidence.candidate.source_commit -ne $expectedCommit -or
    [string]$evidence.candidate.certificate_thumbprint -ne $expectedThumbprint) {
    throw 'Sparse Package Explorer candidate identity does not match approval inputs.'
}
if (-not [bool]$evidence.environment.operator_asserted_clean_user -or
    -not [bool]$evidence.automated_checks.passed -or
    -not [bool]$evidence.automated_checks.package_removed_after_capture -or
    -not [bool]$evidence.automated_checks.legacy_menu_state_unchanged) {
    throw 'Sparse Package Explorer automated checks are incomplete.'
}
if ($evidence.human_review.status -ne 'pending') {
    throw 'Sparse Package Explorer evidence is not pending review.'
}
if ($Reviewer.Trim() -eq [string]$evidence.environment.user) {
    throw 'Reviewer must differ from the Windows account that captured the evidence.'
}
foreach ($entry in @($evidence.files)) {
    $path = Join-Path $root ([string]$entry.file)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Sparse Package Explorer evidence file is missing: $path"
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        [string]$entry.sha256) {
        throw "Sparse Package Explorer evidence file changed after capture: $($entry.file)"
    }
}

$evidence.human_review.status = 'approved'
$evidence.human_review.reviewer = $Reviewer.Trim()
$evidence.human_review.reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
$evidence.human_review.notes = $ReviewNotes.Trim()
foreach ($property in $evidence.human_review.checklist.psobject.Properties) {
    $property.Value = $true
}
Update-JsonFileAtomically `
    -Value $evidence `
    -Path $evidencePath `
    -ExpectedSha256 $evidenceHashBeforeReview `
    -Depth 8 `
    -Label 'Sparse Explorer evidence manifest'
Write-Output "Sparse Package Explorer evidence approved by $($Reviewer.Trim())."
Write-Output "Evidence: $evidencePath"
