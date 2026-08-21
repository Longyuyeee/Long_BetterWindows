#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify automated high-contrast, reduced-motion and combined accessibility evidence.
#>
param(
    [Parameter(Mandatory=$true)] [string[]] $EvidenceDirectories,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputParent = Split-Path -Parent $resolvedOutput
$outputStem = [IO.Path]::GetFileNameWithoutExtension($resolvedOutput)
if ($outputStem -notmatch '^[A-Za-z0-9._-]+$') {
    throw 'Accessibility matrix output name must use portable ASCII characters.'
}
$sourceDirectoryName = "$outputStem.sources"
$sourceDirectory = Join-Path $outputParent $sourceDirectoryName
if ((Test-Path -LiteralPath $resolvedOutput) `
    -or (Test-Path -LiteralPath $sourceDirectory)) {
    throw 'Accessibility matrix output or source bundle already exists.'
}
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$requiredProfiles = @('high_contrast','reduced_motion','combined')
$results = @()
$sourceManifests = @()
$uiaEventProfileCount = 0
foreach ($directory in $EvidenceDirectories) {
    $root = [IO.Path]::GetFullPath($directory)
    $manifestPath = Join-Path $root 'accessibility-evidence.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Manifest was not found: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.classification -ne 'automated_physical_accessibility_evidence') {
        throw "Unexpected evidence classification: $manifestPath"
    }
    if ([int]$manifest.schema_version -ne 4) {
        throw "Accessibility evidence schema version 4 is required: $manifestPath"
    }
    if ([string]$manifest.source_commit -ne $expectedCommit) {
        throw "Accessibility evidence source commit does not match ExpectedSourceCommit: $manifestPath"
    }
    $profile = [string]$manifest.expected_profile
    if ($profile -notin $requiredProfiles) { throw "Unsupported accessibility profile: $profile" }
    if (-not [bool]$manifest.automated_checks_passed) {
        throw "Automated accessibility evidence did not pass: $profile"
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
    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $eventEvidence = $manifest.assistive_technology_events
    $reportedEvents = $report.assistive_technology_events
    if ([string]$eventEvidence.transport -ne 'windows_ui_automation_events' `
        -or -not [bool]$eventEvidence.physical_keyboard_validated `
        -or [int]$eventEvidence.focus_event_count -lt 1 `
        -or [int]$eventEvidence.live_region_event_count -lt 1 `
        -or -not [bool]$reportedEvents.passed `
        -or [int]$reportedEvents.focus_event_count -ne [int]$eventEvidence.focus_event_count `
        -or [int]$reportedEvents.live_region_event_count -ne [int]$eventEvidence.live_region_event_count) {
        throw "Accessibility UIA event evidence is incomplete: $profile"
    }
    $readerName = [string]$manifest.screen_reader.name
    $uiaEventProfileCount++
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $sourceFile = "accessibility-$profile.json"
    $sourceManifests += [ordered]@{
        path = $manifestPath
        file = $sourceFile
    }
    $results += [ordered]@{
        profile = $profile
        source_commit = $expectedCommit
        screen_reader = $readerName
        focus_event_count = [int]$eventEvidence.focus_event_count
        live_region_event_count = [int]$eventEvidence.live_region_event_count
        source_manifest = [ordered]@{
            file = "$sourceDirectoryName/$sourceFile"
            sha256 = $manifestHash
        }
    }
}

$actualProfiles = @($results | ForEach-Object profile | Sort-Object -Unique)
if ($actualProfiles.Count -ne $requiredProfiles.Count -or
    (Compare-Object -ReferenceObject $requiredProfiles -DifferenceObject $actualProfiles).Count -ne 0) {
    throw "Incomplete accessibility matrix. Required: $($requiredProfiles -join ', '); found: $($actualProfiles -join ', ')."
}
$summary = [ordered]@{
    schema_version = 5
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'automated_physical_accessibility_matrix'
    source_commit = $expectedCommit
    required_profiles = $requiredProfiles
    uia_event_profile_count = $uiaEventProfileCount
    passed = $true
    evidence = $results
}
$temporarySourceDirectory = Join-Path $outputParent (
    ".$sourceDirectoryName.$([Guid]::NewGuid().ToString('N')).tmp")
$sourceCommitted = $false
try {
    if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
        [IO.Directory]::CreateDirectory($outputParent) | Out-Null
    }
    [IO.Directory]::CreateDirectory($temporarySourceDirectory) | Out-Null
    foreach ($source in $sourceManifests) {
        Copy-Item -LiteralPath $source.path `
            -Destination (Join-Path $temporarySourceDirectory $source.file)
    }
    [IO.Directory]::Move($temporarySourceDirectory, $sourceDirectory)
    $sourceCommitted = $true
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $resolvedOutput `
        -Depth 6 `
        -Label 'Accessibility matrix summary'
}
catch {
    if ($sourceCommitted -and (Test-Path -LiteralPath $sourceDirectory)) {
        Remove-Item -LiteralPath $sourceDirectory -Recurse -Force
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporarySourceDirectory) {
        Remove-Item -LiteralPath $temporarySourceDirectory -Recurse -Force
    }
}
Write-Output "Accessibility matrix summary: $resolvedOutput"
Write-Output 'Physical accessibility release matrix verified.'
