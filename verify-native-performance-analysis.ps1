#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [string] $ExportDirectory,
    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Resolve-SafeChildPath([string] $Root, [string] $RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) `
        -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Analysis report contains an invalid relative path: $RelativePath"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Analysis report path escapes the evidence directory: $RelativePath"
    }
    return $path
}

$evidenceRoot = Resolve-RepositoryPath $EvidenceDirectory
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
}
$sourceCommit = $ExpectedCommit.Trim().ToLowerInvariant()
$reportPath = Join-Path $evidenceRoot 'native-performance-analysis.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Native performance analysis report was not found: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$exportRoot = if ([string]::IsNullOrWhiteSpace($ExportDirectory)) {
    Split-Path -Parent (Resolve-SafeChildPath -Root $evidenceRoot `
        -RelativePath ([string]$report.export_manifest_file))
}
else {
    Resolve-RepositoryPath $ExportDirectory
}
& (Join-Path $PSScriptRoot 'verify-native-performance-export.ps1') `
    -EvidenceDirectory $evidenceRoot -ExportDirectory $exportRoot `
    -ExpectedCommit $sourceCommit
$rawManifestPath = Join-Path $evidenceRoot 'native-performance-evidence.json'
$rawManifest = Get-Content -LiteralPath $rawManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$exportManifestPath = Join-Path $exportRoot 'native-performance-export.json'
$errors = [Collections.Generic.List[string]]::new()
if ([int]$report.schema_version -ne 1 `
    -or [string]$report.classification -ne 'native_performance_analysis') {
    $errors.Add('Native performance analysis contract is invalid.')
}
if ([string]$report.source_commit -ne $sourceCommit) {
    $errors.Add('Native performance analysis source commit does not match.')
}
if ([string]::IsNullOrWhiteSpace([string]$report.reviewer) `
    -or [string]::IsNullOrWhiteSpace([string]$report.notes)) {
    $errors.Add('Native performance analysis reviewer and notes are required.')
}
if ([string]$report.raw_manifest_sha256 -ne (
        Get-FileHash -LiteralPath $rawManifestPath -Algorithm SHA256
    ).Hash.ToLowerInvariant() `
    -or [string]$report.trace_sha256 -ne ([string]$rawManifest.trace_sha256).ToLowerInvariant()) {
    $errors.Add('Native performance analysis raw capture binding does not match.')
}
$boundExportPath = Resolve-SafeChildPath -Root $evidenceRoot `
    -RelativePath ([string]$report.export_manifest_file)
if ($boundExportPath -ne $exportManifestPath `
    -or [string]$report.export_manifest_sha256 -ne (
        Get-FileHash -LiteralPath $exportManifestPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()) {
    $errors.Add('Native performance analysis WPA export binding does not match.')
}
if (-not [bool]$report.confirmations.cpu_sampled_reviewed `
    -or -not [bool]$report.confirmations.desktop_composition_reviewed `
    -or -not [bool]$report.confirmations.timeline_correlated `
    -or -not [bool]$report.confirmations.no_unresolved_product_hotspot `
    -or [string]$report.analysis_status -ne 'reviewed_pass' `
    -or -not [bool]$report.passed `
    -or [bool]$report.release_gate_passed) {
    $errors.Add('Native performance analysis confirmations or status are incomplete.')
}

$evidence = @($report.evidence_files)
$categories = @($evidence.category)
if ($evidence.Count -lt 2 `
    -or $categories -notcontains 'cpu_sampled' `
    -or $categories -notcontains 'desktop_composition' `
    -or @($evidence.relative_path | Sort-Object -Unique).Count -ne $evidence.Count) {
    $errors.Add('Separate CPU and Desktop Composition evidence files are required.')
}
foreach ($item in $evidence) {
    $path = Resolve-SafeChildPath -Root $evidenceRoot `
        -RelativePath ([string]$item.relative_path)
    if ([IO.Path]::GetExtension($path).ToLowerInvariant() `
        -notin @('.csv', '.xml', '.png', '.jpg', '.jpeg') `
        -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Native performance analysis evidence is invalid: $($item.relative_path)")
        continue
    }
    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -le 0 -or $file.Length -ne [long]$item.size_bytes `
        -or $hash -ne [string]$item.sha256) {
        $errors.Add("Native performance analysis evidence changed: $($item.relative_path)")
    }
}

if ($errors.Count -gt 0) {
    throw ($errors -join [Environment]::NewLine)
}
Write-Host 'Native performance analysis evidence is internally consistent.'
Write-Host 'Independent final approval is still required; release gate remains blocked.'
