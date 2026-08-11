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
    [Parameter(Mandatory=$true)] [switch] $ConfirmManagementTabOrder,
    [Parameter(Mandatory=$true)] [switch] $ConfirmManagementActivation,
    [Parameter(Mandatory=$true)] [switch] $ConfirmManagementModuleCloseMru,
    [switch] $ConfirmScreenReaderAnnouncements,
    [switch] $ConfirmManagementCloseAnnouncements
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
if ([string]::IsNullOrWhiteSpace($Reviewer)) { throw 'Reviewer must not be empty.' }
if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 12) {
    throw 'ReviewNotes must contain at least 12 characters.'
}
if (-not $ConfirmKeyboardNavigation -or -not $ConfirmFocusVisibility -or -not $ConfirmMotionBehavior) {
    throw 'Keyboard navigation, focus visibility and motion behavior confirmations are required.'
}
if (-not $ConfirmManagementTabOrder -or -not $ConfirmManagementActivation `
    -or -not $ConfirmManagementModuleCloseMru) {
    throw 'Management destination order, activation and module close/MRU confirmations are required.'
}

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'accessibility-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Evidence manifest was not found: $manifestPath" }
$manifestHashBeforeReview = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'physical_accessibility_evidence') {
    throw "Unexpected evidence classification: $($manifest.classification)"
}
if ([int]$manifest.schema_version -ne 3) {
    throw 'Accessibility evidence schema version 3 is required. Recapture this candidate.'
}
if ([string]$manifest.human_review.status -ne 'pending') {
    throw 'Accessibility evidence is not pending review.'
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
$eventEvidence = $manifest.assistive_technology_events
$reportedEvents = $report.assistive_technology_events
if ([string]$eventEvidence.transport -ne 'windows_ui_automation_events' `
    -or -not [bool]$eventEvidence.physical_keyboard_validated `
    -or [int]$eventEvidence.focus_event_count -lt 1 `
    -or [int]$eventEvidence.live_region_event_count -lt 1 `
    -or -not [bool]$reportedEvents.passed `
    -or [int]$reportedEvents.focus_event_count -ne [int]$eventEvidence.focus_event_count `
    -or [int]$reportedEvents.live_region_event_count -ne [int]$eventEvidence.live_region_event_count `
    -or [string]$reportedEvents.expected_announcement -ne [string]$eventEvidence.expected_announcement) {
    throw 'Accessibility UIA event evidence is incomplete or inconsistent.'
}

$readerName = [string]$manifest.screen_reader.name
if ($readerName -ne 'None') {
    if (-not [bool]$manifest.screen_reader.process_detected) {
        throw 'The selected screen reader was not detected during capture.'
    }
    if (-not [bool]$eventEvidence.screen_reader_active_during_capture) {
        throw 'UIA event evidence was not captured while the screen reader was active.'
    }
    if (-not $ConfirmScreenReaderAnnouncements) {
        throw 'ConfirmScreenReaderAnnouncements is required for screen-reader evidence.'
    }
    if (-not $ConfirmManagementCloseAnnouncements) {
        throw 'ConfirmManagementCloseAnnouncements is required for screen-reader evidence.'
    }
}

$manifest.human_review.status = 'approved'
$manifest.human_review.reviewer = $Reviewer.Trim()
$manifest.human_review.reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
$manifest.human_review.notes = $ReviewNotes.Trim()
$manifest.human_review.checklist.keyboard_navigation = $true
$manifest.human_review.checklist.focus_visibility = $true
$manifest.human_review.checklist.motion_behavior = $true
$manifest.human_review.checklist.management_destination_tab_order = $true
$manifest.human_review.checklist.management_destination_activation = $true
$manifest.human_review.checklist.management_module_close_mru = $true
$manifest.human_review.checklist.screen_reader_announcements = `
    if ($readerName -eq 'None') { $null } else { $true }
$manifest.human_review.checklist.management_close_announcements = `
    if ($readerName -eq 'None') { $null } else { $true }
Update-JsonFileAtomically `
    -Value $manifest `
    -Path $manifestPath `
    -ExpectedSha256 $manifestHashBeforeReview `
    -Depth 8 `
    -Label 'Accessibility evidence manifest'
Write-Output "Accessibility evidence approved for $ConfirmProfile by $($Reviewer.Trim())."
Write-Output "Manifest: $manifestPath"
