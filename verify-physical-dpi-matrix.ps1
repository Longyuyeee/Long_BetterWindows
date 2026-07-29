#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify the complete approved 100%, 125%, 150%, and 200% physical DPI evidence matrix.
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
    throw 'Physical DPI matrix output name must use portable ASCII characters.'
}
$sourceDirectoryName = "$outputStem.sources"
$sourceDirectory = Join-Path $outputParent $sourceDirectoryName
if ((Test-Path -LiteralPath $resolvedOutput) `
    -or (Test-Path -LiteralPath $sourceDirectory)) {
    throw 'Physical DPI matrix output or source bundle already exists.'
}
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$requiredScales = @(100,125,150,200)
$results = @()
$sourceManifests = @()
foreach ($directory in $EvidenceDirectories) {
    $root = [IO.Path]::GetFullPath($directory)
    $manifestPath = Join-Path $root 'physical-dpi-evidence.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Physical DPI manifest was not found: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.classification -ne 'physical_device_dpi_evidence') {
        throw "Unexpected evidence classification: $manifestPath"
    }
    if ([int]$manifest.schema_version -ne 2) {
        throw "Physical DPI evidence schema version 2 is required: $manifestPath"
    }
    if ([string]$manifest.source_commit -ne $expectedCommit) {
        throw "Physical DPI evidence source commit does not match ExpectedSourceCommit: $manifestPath"
    }
    $scale = [int]$manifest.expected_scale_percent
    if ($scale -notin $requiredScales) { throw "Unsupported release matrix scale: $scale%" }
    if (-not [bool]$manifest.automated_checks_passed) { throw "Automated checks did not pass: $scale%" }
    if ($manifest.human_review.status -ne 'approved') { throw "Human review is not approved: $scale%" }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.human_review.reviewer)) {
        throw "Human reviewer is missing: $scale%"
    }
    $captures = @($manifest.captures)
    if ($captures.Count -ne 8) { throw "Expected 8 captures at $scale%, found $($captures.Count)." }
    if ('main' -notin @($captures | ForEach-Object { [string]$_.view })) {
        throw "Physical DPI evidence does not include the main management-center view at $scale%."
    }
    $checks = $manifest.human_review.checklist
    if (-not [bool]$checks.no_clipping_or_overflow `
        -or -not [bool]$checks.text_and_icons_are_sharp `
        -or -not [bool]$checks.keyboard_focus_is_visible `
        -or -not [bool]$checks.light_and_dark_themes_are_consistent `
        -or -not [bool]$checks.web_plugin_content_is_visible `
        -or -not [bool]$checks.management_center_layout_is_stable `
        -or -not [bool]$checks.management_module_tabs_are_readable) {
        throw "Manual physical DPI checklist is incomplete: $scale%"
    }
    foreach ($capture in $captures) {
        $imagePath = Join-Path $root $capture.file
        $metadataPath = Join-Path $root $capture.metadata_file
        if (-not (Test-Path -LiteralPath $imagePath) -or -not (Test-Path -LiteralPath $metadataPath)) {
            throw "Evidence file is missing at $scale%: $($capture.file)"
        }
        $imageHash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $metadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($imageHash -ne $capture.sha256 -or $metadataHash -ne $capture.metadata_sha256) {
            throw "Evidence hash mismatch at $scale%: $($capture.file)"
        }
        $actualScale = [double]$capture.actual_scale_percent
        if ([Math]::Abs($actualScale - $scale) -gt 0.7) {
            throw "Captured physical scale does not match manifest at $scale%: $actualScale%."
        }
    }
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $sourceFile = "physical-dpi-$scale.json"
    $sourceManifests += [ordered]@{
        path = $manifestPath
        file = $sourceFile
    }
    $results += [ordered]@{
        scale_percent = $scale
        source_commit = $expectedCommit
        reviewer = $manifest.human_review.reviewer
        reviewed_at = $manifest.human_review.reviewed_at
        capture_count = $captures.Count
        source_manifest = [ordered]@{
            file = "$sourceDirectoryName/$sourceFile"
            sha256 = $manifestHash
        }
    }
}

$actualScales = @($results | ForEach-Object { [int]$_.scale_percent } | Sort-Object -Unique)
if ($actualScales.Count -ne $requiredScales.Count -or
    (Compare-Object -ReferenceObject $requiredScales -DifferenceObject $actualScales).Count -ne 0) {
    throw "Incomplete physical DPI matrix. Required: $($requiredScales -join ', '); found: $($actualScales -join ', ')."
}

$summary = [ordered]@{
    schema_version = 3
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_physical_device_dpi_matrix'
    source_commit = $expectedCommit
    required_scales = $requiredScales
    capture_count = ($results `
        | ForEach-Object { [int]$_['capture_count'] } `
        | Measure-Object -Sum).Sum
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
        -Label 'Physical DPI matrix summary'
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
Write-Output "Matrix summary: $resolvedOutput"
Write-Output 'Physical DPI release matrix verified: 32 captures, 4 approved scales.'
