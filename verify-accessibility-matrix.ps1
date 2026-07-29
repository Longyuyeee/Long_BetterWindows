#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify approved high-contrast, reduced-motion and combined accessibility evidence.
#>
param(
    [Parameter(Mandatory=$true)] [string[]] $EvidenceDirectories,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$requiredProfiles = @('high_contrast','reduced_motion','combined')
$results = @()
$screenReaderApprovalCount = 0
foreach ($directory in $EvidenceDirectories) {
    $root = [IO.Path]::GetFullPath($directory)
    $manifestPath = Join-Path $root 'accessibility-evidence.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Manifest was not found: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.classification -ne 'physical_accessibility_evidence') {
        throw "Unexpected evidence classification: $manifestPath"
    }
    if ([int]$manifest.schema_version -ne 2) {
        throw "Accessibility evidence schema version 2 is required: $manifestPath"
    }
    if ([string]$manifest.source_commit -ne $expectedCommit) {
        throw "Accessibility evidence source commit does not match ExpectedSourceCommit: $manifestPath"
    }
    $profile = [string]$manifest.expected_profile
    if ($profile -notin $requiredProfiles) { throw "Unsupported accessibility profile: $profile" }
    if (-not [bool]$manifest.automated_checks_passed -or $manifest.human_review.status -ne 'approved') {
        throw "Accessibility evidence is not approved: $profile"
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.human_review.reviewer)) {
        throw "Human reviewer is missing: $profile"
    }
    $checks = $manifest.human_review.checklist
    if (-not [bool]$checks.keyboard_navigation -or -not [bool]$checks.focus_visibility `
        -or -not [bool]$checks.motion_behavior `
        -or -not [bool]$checks.management_destination_tab_order `
        -or -not [bool]$checks.management_destination_activation `
        -or -not [bool]$checks.management_module_close_mru) {
        throw "Manual accessibility checklist is incomplete: $profile"
    }
    $settings = $manifest.windows_settings
    $settingsMatch = switch ($profile) {
        'high_contrast' { [bool]$settings.high_contrast -and -not [bool]$settings.reduced_motion }
        'reduced_motion' { -not [bool]$settings.high_contrast -and [bool]$settings.reduced_motion }
        'combined' { [bool]$settings.high_contrast -and [bool]$settings.reduced_motion }
    }
    if (-not $settingsMatch) { throw "Captured Windows settings do not match profile: $profile" }

    $reportPath = Join-Path $root $manifest.desktop_ui_report.file
    $logPath = Join-Path $root $manifest.desktop_ui_log.file
    if (-not (Test-Path $reportPath) -or -not (Test-Path $logPath)) {
        throw "Accessibility evidence files are missing: $profile"
    }
    if ((Get-FileHash $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.desktop_ui_report.sha256 `
        -or (Get-FileHash $logPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.desktop_ui_log.sha256) {
        throw "Accessibility evidence hash mismatch: $profile"
    }
    $readerName = [string]$manifest.screen_reader.name
    if ($readerName -ne 'None' -and [bool]$manifest.screen_reader.process_detected `
        -and [bool]$checks.screen_reader_announcements `
        -and [bool]$checks.management_close_announcements) {
        $screenReaderApprovalCount++
    }
    $results += [ordered]@{
        profile = $profile
        source_commit = $expectedCommit
        reviewer = $manifest.human_review.reviewer
        reviewed_at = $manifest.human_review.reviewed_at
        screen_reader = $readerName
        manifest_sha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$actualProfiles = @($results | ForEach-Object profile | Sort-Object -Unique)
if ($actualProfiles.Count -ne $requiredProfiles.Count -or
    (Compare-Object -ReferenceObject $requiredProfiles -DifferenceObject $actualProfiles).Count -ne 0) {
    throw "Incomplete accessibility matrix. Required: $($requiredProfiles -join ', '); found: $($actualProfiles -join ', ')."
}
if ($screenReaderApprovalCount -lt 1) {
    throw 'Accessibility matrix requires at least one approved Narrator or NVDA announcement review.'
}

$summary = [ordered]@{
    schema_version = 2
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_physical_accessibility_matrix'
    source_commit = $expectedCommit
    required_profiles = $requiredProfiles
    screen_reader_approval_count = $screenReaderApprovalCount
    passed = $true
    evidence = $results
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    Write-Output "Accessibility matrix summary: $resolvedOutput"
}
Write-Output 'Physical accessibility release matrix verified.'
