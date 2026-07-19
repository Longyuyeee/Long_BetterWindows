#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify the complete approved 100%, 125%, 150%, and 200% physical DPI evidence matrix.
#>
param(
    [Parameter(Mandatory=$true)] [string[]] $EvidenceDirectories,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$requiredScales = @(100,125,150,200)
$results = @()
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
    $scale = [int]$manifest.expected_scale_percent
    if ($scale -notin $requiredScales) { throw "Unsupported release matrix scale: $scale%" }
    if (-not [bool]$manifest.automated_checks_passed) { throw "Automated checks did not pass: $scale%" }
    if ($manifest.human_review.status -ne 'approved') { throw "Human review is not approved: $scale%" }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.human_review.reviewer)) {
        throw "Human reviewer is missing: $scale%"
    }
    $captures = @($manifest.captures)
    if ($captures.Count -ne 8) { throw "Expected 8 captures at $scale%, found $($captures.Count)." }
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
    $results += [ordered]@{
        scale_percent = $scale
        reviewer = $manifest.human_review.reviewer
        reviewed_at = $manifest.human_review.reviewed_at
        capture_count = $captures.Count
        manifest_sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$actualScales = @($results | ForEach-Object { [int]$_.scale_percent } | Sort-Object -Unique)
if ($actualScales.Count -ne $requiredScales.Count -or
    (Compare-Object -ReferenceObject $requiredScales -DifferenceObject $actualScales).Count -ne 0) {
    throw "Incomplete physical DPI matrix. Required: $($requiredScales -join ', '); found: $($actualScales -join ', ')."
}

$summary = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_physical_device_dpi_matrix'
    required_scales = $requiredScales
    capture_count = ($results | Measure-Object -Property capture_count -Sum).Sum
    passed = $true
    evidence = $results
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    Write-Output "Matrix summary: $resolvedOutput"
}
Write-Output 'Physical DPI release matrix verified: 32 captures, 4 approved scales.'
