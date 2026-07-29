#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Aggregate approved external evidence into one immutable release decision.
.DESCRIPTION
  This verifier does not collect or approve evidence. Each input must be a passing
  summary produced by its dedicated verifier, and the marketplace input must be a
  complete HTTPS deploy, public verification, rollback, and re-verification rehearsal.
#>
param(
    [Parameter(Mandatory=$true)] [string] $ReleaseManifestPath,
    [Parameter(Mandatory=$true)] [string] $DownloadGatePath,
    [Parameter(Mandatory=$true)] [string] $CleanEnvironmentGatePath,
    [Parameter(Mandatory=$true)] [string] $PhysicalDpiGatePath,
    [Parameter(Mandatory=$true)] [string] $AccessibilityGatePath,
    [Parameter(Mandatory=$true)] [string] $MarketplaceRehearsalPath,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [Parameter(Mandatory=$true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

function Read-GateJson([string] $path, [string] $label) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$label was not found: $resolved"
    }
    try {
        $document = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "$label is not valid JSON: $resolved"
    }
    return [ordered]@{
        path = $resolved
        document = $document
        sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Assert-Classification($gate, [string] $expected, [string] $label) {
    if ([string]$gate.document.classification -ne $expected -or -not [bool]$gate.document.passed) {
        throw "$label is not a passing $expected document."
    }
}

function Assert-ExactSet(
    [object[]] $actual,
    [object[]] $required,
    [string] $label) {
    $actualValues = @($actual | Sort-Object -Unique)
    $requiredValues = @($required | Sort-Object -Unique)
    if ($actualValues.Count -ne $requiredValues.Count `
        -or (Compare-Object -ReferenceObject $requiredValues `
            -DifferenceObject $actualValues).Count -ne 0) {
        throw "$label is incomplete. Required: $($requiredValues -join ', '); found: $($actualValues -join ', ')."
    }
}

function Assert-PhysicalDpiContract($document, [string] $sourceCommit) {
    $requiredScales = @(100,125,150,200)
    if ([int]$document.schema_version -ne 2) {
        throw 'Physical DPI gate schema version 2 is required.'
    }
    if ([int]$document.capture_count -ne 32) {
        throw 'Physical DPI gate must contain exactly 32 captures.'
    }
    Assert-ExactSet @($document.required_scales | ForEach-Object { [int]$_ }) `
        $requiredScales 'Physical DPI required scales'
    $evidence = @($document.evidence)
    if ($evidence.Count -ne 4) {
        throw 'Physical DPI gate must contain exactly four evidence entries.'
    }
    Assert-ExactSet @($evidence | ForEach-Object { [int]$_.scale_percent }) `
        $requiredScales 'Physical DPI evidence scales'
    foreach ($entry in $evidence) {
        if ([string]$entry.source_commit -ne $sourceCommit `
            -or [int]$entry.capture_count -ne 8) {
            throw 'Physical DPI evidence entry does not match the candidate or eight-capture contract.'
        }
    }
}

function Assert-AccessibilityContract($document, [string] $sourceCommit) {
    $requiredProfiles = @('high_contrast','reduced_motion','combined')
    if ([int]$document.schema_version -ne 2) {
        throw 'Accessibility gate schema version 2 is required.'
    }
    if ([int]$document.screen_reader_approval_count -lt 1) {
        throw 'Accessibility gate requires at least one screen-reader approval.'
    }
    Assert-ExactSet @($document.required_profiles | ForEach-Object { [string]$_ }) `
        $requiredProfiles 'Accessibility required profiles'
    $evidence = @($document.evidence)
    if ($evidence.Count -ne 3) {
        throw 'Accessibility gate must contain exactly three evidence entries.'
    }
    Assert-ExactSet @($evidence | ForEach-Object { [string]$_.profile }) `
        $requiredProfiles 'Accessibility evidence profiles'
    foreach ($entry in $evidence) {
        if ([string]$entry.source_commit -ne $sourceCommit) {
            throw 'Accessibility evidence entry does not match the candidate.'
        }
    }
}

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "External release decision already exists: $resolvedOutput"
}

$release = Read-GateJson $ReleaseManifestPath 'Release Manifest'
$download = Read-GateJson $DownloadGatePath 'Release-download gate'
$clean = Read-GateJson $CleanEnvironmentGatePath 'Clean-environment gate'
$dpi = Read-GateJson $PhysicalDpiGatePath 'Physical DPI gate'
$accessibility = Read-GateJson $AccessibilityGatePath 'Accessibility gate'
$marketplace = Read-GateJson $MarketplaceRehearsalPath 'Marketplace rehearsal'

Assert-Classification $download 'approved_release_download_gate' 'Release-download gate'
Assert-Classification $clean 'approved_clean_windows_release_gate' 'Clean-environment gate'
Assert-Classification $dpi 'approved_physical_device_dpi_matrix' 'Physical DPI gate'
Assert-Classification $accessibility 'approved_physical_accessibility_matrix' 'Accessibility gate'
Assert-Classification $marketplace 'marketplace_https_rehearsal' 'Marketplace rehearsal'

if ([string]$release.document.commit -ne $expectedCommit) {
    throw 'Release Manifest source commit does not match ExpectedSourceCommit.'
}
if ([string]$release.document.distribution_channel -ne $ExpectedDistributionChannel `
    -or -not [bool]$release.document.release_eligible `
    -or ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$release.document.signed) `
    -or ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$release.document.signed)) {
    throw 'Release Manifest does not match the eligible expected distribution channel.'
}

foreach ($gate in @($download, $clean, $dpi, $accessibility)) {
    if ([string]$gate.document.source_commit -ne $expectedCommit) {
        throw "External gate source commit mismatch: $($gate.document.classification)"
    }
}
Assert-PhysicalDpiContract $dpi.document $expectedCommit
Assert-AccessibilityContract $accessibility.document $expectedCommit
foreach ($gate in @($download, $clean)) {
    if ([string]$gate.document.distribution_channel -ne $ExpectedDistributionChannel) {
        throw "External gate distribution channel mismatch: $($gate.document.classification)"
    }
}

$packageFile = [string]$download.document.package_file
$packageSha256 = ([string]$download.document.package_sha256).ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($packageFile) -or $packageSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Release-download gate package identity is invalid.'
}
if (([string]$clean.document.package_sha256).ToLowerInvariant() -ne $packageSha256) {
    throw 'Clean-environment and release-download gates refer to different packages.'
}
$manifestPackages = @($release.document.packages | Where-Object {
    [string]$_.file -eq $packageFile -and ([string]$_.sha256).ToLowerInvariant() -eq $packageSha256
})
if ($manifestPackages.Count -ne 1) {
    throw 'Approved package identity does not match exactly one Release Manifest package.'
}

$downloadOperator = ([string]$download.document.operator).Trim()
$downloadReviewer = ([string]$download.document.reviewer).Trim()
if ([string]::IsNullOrWhiteSpace($downloadOperator) `
    -or [string]::IsNullOrWhiteSpace($downloadReviewer) `
    -or $downloadOperator -eq $downloadReviewer) {
    throw 'Release-download gate does not preserve independent operator and reviewer identities.'
}
if ([string]::IsNullOrWhiteSpace(([string]$clean.document.reviewer).Trim())) {
    throw 'Clean-environment gate reviewer is missing.'
}

$rehearsal = $marketplace.document
$destination = $null
if (-not [uri]::TryCreate([string]$rehearsal.destination, [UriKind]::Absolute, [ref]$destination) `
    -or $destination.Scheme -ne 'https') {
    throw 'Marketplace rehearsal destination must be absolute HTTPS.'
}
if ([bool]$rehearsal.preflight_only `
    -or [string]::IsNullOrWhiteSpace([string]$rehearsal.release_id) `
    -or -not [bool]$rehearsal.preflight_dry_run_verified `
    -or -not [bool]$rehearsal.baseline_verified `
    -or -not [bool]$rehearsal.deployment_completed `
    -or -not [bool]$rehearsal.deployment_verified `
    -or -not [bool]$rehearsal.rollback_completed `
    -or -not [bool]$rehearsal.rollback_verified `
    -or -not [string]::IsNullOrWhiteSpace([string]$rehearsal.failure) `
    -or -not [string]::IsNullOrWhiteSpace([string]$rehearsal.rollback_failure) `
    -or -not [string]::IsNullOrWhiteSpace([string]$rehearsal.rollback_verification_failure)) {
    throw 'Marketplace rehearsal is not a complete passing deploy and rollback cycle.'
}

$decision = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'external_release_gate_decision'
    passed = $true
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    signed = [bool]$release.document.signed
    package = [ordered]@{
        file = $packageFile
        sha256 = $packageSha256
    }
    independent_review = [ordered]@{
        download_operator = $downloadOperator
        download_reviewer = $downloadReviewer
        clean_environment_reviewer = [string]$clean.document.reviewer
    }
    marketplace = [ordered]@{
        release_id = [string]$rehearsal.release_id
        destination_host = $destination.DnsSafeHost
        registry_committed_last = [bool]$rehearsal.preflight_dry_run_verified
        deployment_verified = [bool]$rehearsal.deployment_verified
        rollback_verified = [bool]$rehearsal.rollback_verified
    }
    evidence_contract = [ordered]@{
        physical_dpi_schema_version = [int]$dpi.document.schema_version
        physical_dpi_capture_count = [int]$dpi.document.capture_count
        accessibility_schema_version = [int]$accessibility.document.schema_version
        screen_reader_approval_count =
            [int]$accessibility.document.screen_reader_approval_count
    }
    inputs = [ordered]@{
        release_manifest_sha256 = $release.sha256
        release_download_gate_sha256 = $download.sha256
        clean_environment_gate_sha256 = $clean.sha256
        physical_dpi_gate_sha256 = $dpi.sha256
        accessibility_gate_sha256 = $accessibility.sha256
        marketplace_rehearsal_sha256 = $marketplace.sha256
    }
}

$parent = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    [IO.Directory]::CreateDirectory($parent) | Out-Null
}
$decision | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
Write-Output 'External release gate verified.'
Write-Output "Decision: $resolvedOutput"
