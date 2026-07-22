#!/usr/bin/env pwsh
<# .SYNOPSIS Approve interactive install, upgrade, rollback and uninstall evidence. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $ReviewNotes,
    [Parameter(Mandatory=$true)] [switch] $ConfirmFirstStart,
    [Parameter(Mandatory=$true)] [switch] $ConfirmTrayIcon,
    [Parameter(Mandatory=$true)] [switch] $ConfirmGlobalHotkey,
    [Parameter(Mandatory=$true)] [switch] $ConfirmWebViewRuntime,
    [Parameter(Mandatory=$true)] [switch] $ConfirmParallelUpgradeDataPreserved,
    [Parameter(Mandatory=$true)] [switch] $ConfirmRollbackToPreviousVersion,
    [Parameter(Mandatory=$true)] [switch] $ConfirmUninstallIntegrationsRemoved
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Reviewer)) { throw 'Reviewer must not be empty.' }
if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 12) {
    throw 'ReviewNotes must contain at least 12 characters.'
}
$required = @(
    $ConfirmFirstStart, $ConfirmTrayIcon, $ConfirmGlobalHotkey, $ConfirmWebViewRuntime,
    $ConfirmParallelUpgradeDataPreserved, $ConfirmRollbackToPreviousVersion,
    $ConfirmUninstallIntegrationsRemoved
)
if ($required -contains $false) { throw 'Every clean-environment lifecycle confirmation is required.' }

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'clean-environment-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'clean_windows_release_evidence') { throw 'Unexpected evidence classification.' }
if ([string]$manifest.release.source_commit -ne $expectedCommit) {
    throw 'Clean-environment evidence source commit does not match ExpectedSourceCommit.'
}
if (-not [bool]$manifest.environment.operator_asserted_clean_user) { throw 'The operator did not assert a clean Windows user.' }
if (-not [bool]$manifest.automated_checks.passed) { throw 'Automated release checks did not pass.' }
if ($Reviewer.Trim() -eq [string]$manifest.environment.user) {
    throw 'Reviewer must differ from the Windows account that captured the evidence.'
}
foreach ($entry in @(
    $manifest.release.release_manifest,
    $manifest.automated_checks.desktop_ui_report,
    $manifest.automated_checks.desktop_ui_log,
    $manifest.automated_checks.command_log
)) {
    $path = Join-Path $root ([string]$entry.file)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Evidence file is missing: $path" }
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
        throw 'Clean-environment evidence changed after capture.'
    }
}

$manifest.human_review.status = 'approved'
$manifest.human_review.reviewer = $Reviewer.Trim()
$manifest.human_review.reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
$manifest.human_review.notes = $ReviewNotes.Trim()
foreach ($property in $manifest.human_review.checklist.psobject.Properties) { $property.Value = $true }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Clean-environment lifecycle evidence approved by $($Reviewer.Trim())."
Write-Output "Manifest: $manifestPath"
