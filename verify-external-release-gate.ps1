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
    [string] $OutputPath,
    [switch] $PreflightOnly
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

function Read-PortableMatrixSource($gate, $entry, [string] $label) {
    $file = [string]$entry.file
    $expectedDirectory = [IO.Path]::GetFileNameWithoutExtension(
        $gate.path) + '.sources/'
    if ($file -notmatch '^[A-Za-z0-9._-]+\.sources/[A-Za-z0-9._-]+\.json$' `
        -or -not $file.StartsWith(
            $expectedDirectory,
            [StringComparison]::OrdinalIgnoreCase) `
        -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "$label portable source identity is incomplete."
    }
    $relativePath = $file.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $path = Join-Path (Split-Path -Parent $gate.path) $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) `
        -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).
            Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
        throw "$label portable source hash mismatch: $file"
    }
    try {
        return Get-Content -LiteralPath $path -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "$label portable source is not valid JSON: $file"
    }
}

function Assert-PhysicalDpiContract($gate, [string] $sourceCommit) {
    $document = $gate.document
    $requiredScales = @(100,125,150,200)
    if ([int]$document.schema_version -ne 3) {
        throw 'Physical DPI gate schema version 3 is required.'
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
    $sourceFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $evidence) {
        if ([string]$entry.source_commit -ne $sourceCommit `
            -or [int]$entry.capture_count -ne 8) {
            throw 'Physical DPI evidence entry does not match the candidate or eight-capture contract.'
        }
        if (-not $sourceFiles.Add([string]$entry.source_manifest.file)) {
            throw 'Physical DPI portable source is duplicated.'
        }
        $source = Read-PortableMatrixSource `
            $gate $entry.source_manifest 'Physical DPI evidence'
        if ([int]$source.schema_version -ne 2 `
            -or [string]$source.classification -ne 'physical_device_dpi_evidence' `
            -or [string]$source.source_commit -ne $sourceCommit `
            -or [int]$source.expected_scale_percent -ne [int]$entry.scale_percent `
            -or [string]$source.human_review.status -ne 'approved') {
            throw 'Physical DPI portable source content does not match its summary.'
        }
    }
}

function Assert-AccessibilityContract($gate, [string] $sourceCommit) {
    $document = $gate.document
    $requiredProfiles = @('high_contrast','reduced_motion','combined')
    if ([int]$document.schema_version -ne 3) {
        throw 'Accessibility gate schema version 3 is required.'
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
    $sourceFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $evidence) {
        if ([string]$entry.source_commit -ne $sourceCommit) {
            throw 'Accessibility evidence entry does not match the candidate.'
        }
        if (-not $sourceFiles.Add([string]$entry.source_manifest.file)) {
            throw 'Accessibility portable source is duplicated.'
        }
        $source = Read-PortableMatrixSource `
            $gate $entry.source_manifest 'Accessibility evidence'
        if ([int]$source.schema_version -ne 2 `
            -or [string]$source.classification -ne 'physical_accessibility_evidence' `
            -or [string]$source.source_commit -ne $sourceCommit `
            -or [string]$source.expected_profile -ne [string]$entry.profile `
            -or [string]$source.human_review.status -ne 'approved') {
            throw 'Accessibility portable source content does not match its summary.'
        }
    }
}

function Assert-HashLockedSource($gate, $entry, [string] $label) {
    $file = [string]$entry.file
    if ([string]::IsNullOrWhiteSpace($file) `
        -or [IO.Path]::GetFileName($file) -ne $file `
        -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "$label source identity is incomplete."
    }
    $path = Join-Path (Split-Path -Parent $gate.path) $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) `
        -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).
            Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
        throw "$label source hash mismatch: $file"
    }
}

function Assert-DownloadContract($gate) {
    $document = $gate.document
    if ([int]$document.schema_version -ne 2 `
        -or [string]::IsNullOrWhiteSpace([string]$document.download_host)) {
        throw 'Release-download gate summary contract is incomplete.'
    }
    Assert-HashLockedSource $gate $document.evidence 'Release-download evidence'
    Assert-HashLockedSource $gate $document.approval 'Release-download approval'
}

function Assert-CleanEnvironmentContract(
    $gate,
    [string] $distributionChannel) {
    $document = $gate.document
    if ([int]$document.schema_version -ne 2 `
        -or [string]::IsNullOrWhiteSpace([string]$document.environment_label) `
        -or [bool]$document.signed -ne ($distributionChannel -eq 'signed')) {
        throw 'Clean-environment gate summary contract is incomplete.'
    }
    if ([string]$document.evidence_manifest.file -ne 'clean-environment-evidence.json') {
        throw 'Clean-environment evidence source identity is incomplete.'
    }
    Assert-HashLockedSource $gate $document.evidence_manifest 'Clean-environment evidence'
}

function Assert-MarketplaceEvidenceContract($gate) {
    if ([int]$gate.document.schema_version -ne 2) {
        throw 'Marketplace rehearsal schema version 2 is required.'
    }
    $completedAt = [DateTimeOffset]::MinValue
    if (-not [bool]$gate.document.deployment_started `
        -or -not [DateTimeOffset]::TryParse(
            [string]$gate.document.completed_at,
            [ref]$completedAt)) {
        throw 'Marketplace rehearsal lifecycle metadata is incomplete.'
    }
    $requiredEvidence = [ordered]@{
        preflight_dry_run = 'preflight-dry-run.json'
        baseline_verification = 'baseline-verification.json'
        deployment = 'deployment.json'
        deployed_verification = 'deployed-verification.json'
        rollback_verification = 'rollback-verification.json'
    }
    $evidence = $gate.document.evidence
    if ($null -eq $evidence) {
        throw 'Marketplace rehearsal evidence entries are missing.'
    }
    $root = Split-Path -Parent $gate.path
    foreach ($required in $requiredEvidence.GetEnumerator()) {
        $property = $evidence.psobject.Properties[$required.Key]
        $entry = if ($null -eq $property) { $null } else { $property.Value }
        if ($null -eq $entry -or [string]$entry.file -ne $required.Value `
            -or [string]$entry.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Marketplace rehearsal evidence entry is incomplete: $($required.Key)"
        }
        $path = Join-Path $root ([string]$entry.file)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) `
            -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).
                Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
            throw "Marketplace rehearsal evidence hash mismatch: $($required.Key)"
        }
    }
}

function Assert-ReleaseManifestContract(
    $document,
    [string] $sourceCommit,
    [string] $distributionChannel) {
    $createdAt = [DateTimeOffset]::MinValue
    if ([int]$document.schema_version -ne 1 `
        -or [string]$document.product -ne 'Long Assistant' `
        -or [string]$document.version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$' `
        -or [string]$document.runtime -ne 'win-x64' `
        -or -not [DateTimeOffset]::TryParse([string]$document.created_at, [ref]$createdAt) `
        -or [bool]$document.source_dirty) {
        throw 'Release Manifest candidate identity contract is incomplete.'
    }
    if ($distributionChannel -eq 'unsigned') {
        if ([string]$document.publisher_identity -ne 'unverified' `
            -or [string]::IsNullOrWhiteSpace([string]$document.security_notice)) {
            throw 'Unsigned Release Manifest publisher disclosure is incomplete.'
        }
    }
    else {
        if ([string]$document.publisher_identity -ne 'authenticode' `
            -or [string]$document.signing.source_commit -ne $sourceCommit `
            -or [string]$document.signing.certificate_thumbprint -notmatch '^[0-9a-fA-F]{40}$') {
            throw 'Signed Release Manifest publisher identity is incomplete.'
        }
    }

    $packages = @($document.packages)
    if ($packages.Count -lt 1 -or $packages.Count -gt 2) {
        throw 'Release Manifest must contain one or two package entries.'
    }
    $fileNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $kinds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $packages) {
        $file = [string]$package.file
        $kind = [string]$package.kind
        if ([string]::IsNullOrWhiteSpace($file) `
            -or [IO.Path]::GetFileName($file) -ne $file `
            -or -not $fileNames.Add($file) `
            -or $kind -notin @('self-contained','framework-dependent') `
            -or -not $kinds.Add($kind) `
            -or [string]$package.sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [long]$package.bytes -le 0 `
            -or [int]$package.plugins -ne 25 `
            -or [int]$package.manifests -ne 25 `
            -or [int]$package.unique_plugin_ids -ne 25 `
            -or [int]$package.commands -ne 42 `
            -or [int]$package.command_smoke_exit_code -ne 0 `
            -or [int]$package.added_webview_processes -ne 0) {
            throw 'Release Manifest package inventory is invalid.'
        }
    }

    $installers = @($document.installers)
    if ($installers.Count -gt 1) {
        throw 'Release Manifest must contain at most one installer entry.'
    }
    foreach ($installer in $installers) {
        $file = [string]$installer.file
        if ([string]::IsNullOrWhiteSpace($file) `
            -or [IO.Path]::GetFileName($file) -ne $file `
            -or -not $fileNames.Add($file) `
            -or [string]$installer.kind -ne 'installer' `
            -or [string]$installer.format -ne 'inno-setup-exe' `
            -or [string]$installer.install_scope -ne 'current-user' `
            -or [bool]$installer.requires_elevation `
            -or [string]$installer.sha256 -notmatch '^[0-9a-f]{64}$' `
            -or [long]$installer.bytes -le 0 `
            -or [int]$installer.plugins -ne 25 `
            -or [int]$installer.commands -ne 42 `
            -or [bool]$installer.signed -ne ($distributionChannel -eq 'signed')) {
            throw 'Release Manifest installer inventory is invalid.'
        }
    }
}

function Assert-ReleaseArtifactFiles($releaseGate) {
    $root = Split-Path -Parent $releaseGate.path
    $artifacts = @($releaseGate.document.packages) +
        @($releaseGate.document.installers)
    foreach ($artifact in $artifacts) {
        $file = [string]$artifact.file
        $path = Join-Path $root $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Release artifact file was not found: $file"
        }
        $item = Get-Item -LiteralPath $path
        if ([long]$item.Length -ne [long]$artifact.bytes) {
            throw "Release artifact file size does not match the Manifest: $file"
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).
            Hash.ToLowerInvariant()
        if ($actualHash -ne ([string]$artifact.sha256).ToLowerInvariant()) {
            throw "Release artifact file hash does not match the Manifest: $file"
        }
    }

    $checksumPath = Join-Path $root 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw 'Release checksum file was not found: SHA256SUMS.txt'
    }
    $checksums = [Collections.Generic.Dictionary[string,string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($line in @(Get-Content -LiteralPath $checksumPath -Encoding UTF8)) {
        if ($line -notmatch '^([0-9a-f]{64})  ([^\\/]+)$' `
            -or $checksums.ContainsKey($Matches[2])) {
            throw 'Release checksum file contains an invalid or duplicate entry.'
        }
        $checksums.Add($Matches[2], $Matches[1])
    }
    if ($checksums.Count -ne $artifacts.Count) {
        throw 'Release checksum file does not contain the exact Manifest artifact set.'
    }
    foreach ($artifact in $artifacts) {
        $checksum = ''
        if (-not $checksums.TryGetValue([string]$artifact.file, [ref]$checksum) `
            -or $checksum -ne ([string]$artifact.sha256).ToLowerInvariant()) {
            throw 'Release checksum file does not match the Manifest artifacts.'
        }
    }
}

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$resolvedOutput = $null
if ($PreflightOnly) {
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        throw 'PreflightOnly does not accept OutputPath and never writes a decision.'
    }
}
else {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        throw 'OutputPath is required unless PreflightOnly is specified.'
    }
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $resolvedOutput) {
        throw "External release decision already exists: $resolvedOutput"
    }
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
Assert-ReleaseManifestContract `
    $release.document `
    $expectedCommit `
    $ExpectedDistributionChannel
Assert-ReleaseArtifactFiles $release

foreach ($gate in @($download, $clean, $dpi, $accessibility)) {
    if ([string]$gate.document.source_commit -ne $expectedCommit) {
        throw "External gate source commit mismatch: $($gate.document.classification)"
    }
}
Assert-DownloadContract $download
Assert-CleanEnvironmentContract $clean $ExpectedDistributionChannel
Assert-PhysicalDpiContract $dpi $expectedCommit
Assert-AccessibilityContract $accessibility $expectedCommit
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
Assert-MarketplaceEvidenceContract $marketplace

$decision = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = if ($PreflightOnly) {
        'external_release_gate_preflight'
    } else {
        'external_release_gate_decision'
    }
    passed = $true
    preflight_only = [bool]$PreflightOnly
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    signed = [bool]$release.document.signed
    package = [ordered]@{
        file = $packageFile
        sha256 = $packageSha256
    }
    candidate = [ordered]@{
        manifest_schema_version = [int]$release.document.schema_version
        version = [string]$release.document.version
        runtime = [string]$release.document.runtime
        source_dirty = [bool]$release.document.source_dirty
        package_count = @($release.document.packages).Count
        installer_count = @($release.document.installers).Count
        artifact_files_verified = $true
        checksum_file_verified = $true
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
        download_schema_version = [int]$download.document.schema_version
        clean_environment_schema_version = [int]$clean.document.schema_version
        marketplace_rehearsal_schema_version = [int]$marketplace.document.schema_version
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

if ($PreflightOnly) {
    $decision | ConvertTo-Json -Depth 7
    return
}
$parent = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    [IO.Directory]::CreateDirectory($parent) | Out-Null
}
$decision | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
Write-Output 'External release gate verified.'
Write-Output "Decision: $resolvedOutput"
