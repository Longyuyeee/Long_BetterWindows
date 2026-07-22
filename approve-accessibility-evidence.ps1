#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Approve accessibility evidence after manual keyboard and assistive-technology review.
#>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('high_contrast','reduced_motion','combined')] [string] $ConfirmProfile,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $ReviewNotes,
    [Parameter(Mandatory=$true)] [switch] $ConfirmKeyboardNavigation,
    [Parameter(Mandatory=$true)] [switch] $ConfirmFocusVisibility,
    [Parameter(Mandatory=$true)] [switch] $ConfirmMotionBehavior,
    [switch] $ConfirmScreenReaderAnnouncements
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Reviewer)) { throw 'Reviewer must not be empty.' }
if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 12) {
    throw 'ReviewNotes must contain at least 12 characters.'
}
if (-not $ConfirmKeyboardNavigation -or -not $ConfirmFocusVisibility -or -not $ConfirmMotionBehavior) {
    throw 'Keyboard navigation, focus visibility and motion behavior confirmations are required.'
}

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'accessibility-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'physical_accessibility_evidence') {
    throw "Unexpected evidence classification: $($manifest.classification)"
}
if ([string]$manifest.source_commit -ne $expectedCommit) {
    throw 'Accessibility evidence source commit does not match ExpectedSourceCommit.'
}
if (-not [bool]$manifest.automated_checks_passed) { throw 'Automated accessibility checks did not pass.' }
if ([string]$manifest.expected_profile -ne $ConfirmProfile) {
    throw "Profile confirmation mismatch. Evidence=$($manifest.expected_profile), confirmation=$ConfirmProfile."
}

$reportPath = Join-Path $root $manifest.desktop_ui_report.file
$logPath = Join-Path $root $manifest.desktop_ui_log.file
if (-not (Test-Path -LiteralPath $reportPath) -or -not (Test-Path -LiteralPath $logPath)) {
    throw 'Desktop UI evidence files are missing.'
}
if ((Get-FileHash $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.desktop_ui_report.sha256 `
    -or (Get-FileHash $logPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.desktop_ui_log.sha256) {
    throw 'Desktop UI evidence changed after capture.'
}
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [bool]$report.passed) { throw 'Desktop UI report is not passing.' }

$readerName = [string]$manifest.screen_reader.name
if ($readerName -ne 'None') {
    if (-not [bool]$manifest.screen_reader.process_detected) {
        throw 'The selected screen reader was not detected during capture.'
    }
    if (-not $ConfirmScreenReaderAnnouncements) {
        throw 'ConfirmScreenReaderAnnouncements is required for screen-reader evidence.'
    }
}

$manifest.human_review.status = 'approved'
$manifest.human_review.reviewer = $Reviewer.Trim()
$manifest.human_review.reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
$manifest.human_review.notes = $ReviewNotes.Trim()
$manifest.human_review.checklist.keyboard_navigation = $true
$manifest.human_review.checklist.focus_visibility = $true
$manifest.human_review.checklist.motion_behavior = $true
$manifest.human_review.checklist.screen_reader_announcements = `
    if ($readerName -eq 'None') { $null } else { $true }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Accessibility evidence approved for $ConfirmProfile by $($Reviewer.Trim())."
Write-Output "Manifest: $manifestPath"
