#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Approve an existing physical DPI evidence set after a human visual review.
#>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet(100,125,150,200,250)] [int] $ConfirmScalePercent,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $ReviewNotes,
    [Parameter(Mandatory=$true)] [switch] $ConfirmVisualReview
)

$ErrorActionPreference = 'Stop'
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
if (-not $ConfirmVisualReview) { throw 'ConfirmVisualReview must be supplied after inspecting all captures.' }
if ([string]::IsNullOrWhiteSpace($Reviewer)) { throw 'Reviewer must not be empty.' }
if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 8) {
    throw 'ReviewNotes must contain at least 8 characters.'
}

$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'physical-dpi-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'physical_device_dpi_evidence') {
    throw "Unexpected evidence classification: $($manifest.classification)"
}
if ([string]$manifest.source_commit -ne $expectedCommit) {
    throw 'Physical DPI evidence source commit does not match ExpectedSourceCommit.'
}
if (-not [bool]$manifest.automated_checks_passed) { throw 'Automated physical DPI checks did not pass.' }
if ([int]$manifest.expected_scale_percent -ne $ConfirmScalePercent) {
    throw "Scale confirmation mismatch. Evidence is $($manifest.expected_scale_percent)%, confirmation is $ConfirmScalePercent%."
}

$captures = @($manifest.captures)
if ($captures.Count -ne 8) { throw "Expected 8 captures, found $($captures.Count)." }
foreach ($capture in $captures) {
    $imagePath = Join-Path $root $capture.file
    $metadataPath = Join-Path $root $capture.metadata_file
    if (-not (Test-Path -LiteralPath $imagePath) -or -not (Test-Path -LiteralPath $metadataPath)) {
        throw "Evidence file is missing: $($capture.file)"
    }
    $imageHash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $metadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($imageHash -ne $capture.sha256 -or $metadataHash -ne $capture.metadata_sha256) {
        throw "Evidence changed after capture: $($capture.file)"
    }
}

$manifest.human_review.status = 'approved'
$manifest.human_review.reviewer = $Reviewer.Trim()
$manifest.human_review.reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
$manifest.human_review.notes = $ReviewNotes.Trim()
$manifest.human_review.checklist.no_clipping_or_overflow = $true
$manifest.human_review.checklist.text_and_icons_are_sharp = $true
$manifest.human_review.checklist.keyboard_focus_is_visible = $true
$manifest.human_review.checklist.light_and_dark_themes_are_consistent = $true
$manifest.human_review.checklist.web_plugin_content_is_visible = $true
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output "Physical DPI evidence approved at $ConfirmScalePercent% by $($Reviewer.Trim())."
Write-Output "Manifest: $manifestPath"
