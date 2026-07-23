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
