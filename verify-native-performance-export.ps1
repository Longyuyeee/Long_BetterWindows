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
        throw "Export manifest contains an invalid relative path: $RelativePath"
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Export manifest path escapes the export directory: $RelativePath"
    }
    return $resolved
}

$evidenceRoot = Resolve-RepositoryPath $EvidenceDirectory
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
}
& (Join-Path $PSScriptRoot 'verify-native-performance-evidence.ps1') `
    -EvidenceDirectory $evidenceRoot -ExpectedCommit $ExpectedCommit

$exportRoot = if ([string]::IsNullOrWhiteSpace($ExportDirectory)) {
    Join-Path $evidenceRoot 'wpa-export'
}
else {
    Resolve-RepositoryPath $ExportDirectory
}
$evidencePrefix = $evidenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $exportRoot.StartsWith($evidencePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'WPA export directory must remain under the native performance evidence directory.'
}
$manifestPath = Join-Path $exportRoot 'native-performance-export.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "WPA export manifest was not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$rawManifestPath = Join-Path $evidenceRoot 'native-performance-evidence.json'
$rawManifest = Get-Content -LiteralPath $rawManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$errors = [Collections.Generic.List[string]]::new()
if ([int]$manifest.schema_version -ne 1 `
    -or [string]$manifest.classification -ne 'native_performance_wpa_export') {
    $errors.Add('WPA export manifest contract is invalid.')
}
if ([string]$manifest.source_commit -ne $ExpectedCommit.Trim().ToLowerInvariant()) {
    $errors.Add('WPA export source commit does not match.')
}
if ([string]$manifest.raw_manifest_sha256 -ne (
        Get-FileHash -LiteralPath $rawManifestPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()) {
    $errors.Add('WPA export raw manifest binding does not match.')
}
if ([string]$manifest.trace_sha256 -ne ([string]$rawManifest.trace_sha256).ToLowerInvariant()) {
    $errors.Add('WPA export trace binding does not match.')
}
if ([string]$manifest.analysis_status -ne 'pending_review' `
    -or [bool]$manifest.release_gate_passed) {
    $errors.Add('WPA export must remain pending_review and cannot pass the release gate.')
}
$profilePath = Resolve-SafeChildPath -Root $exportRoot `
    -RelativePath ([string]$manifest.profile_file)
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf) `
    -or (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash.ToLowerInvariant() `
        -ne [string]$manifest.profile_sha256) {
    $errors.Add('WPA export profile is missing or changed.')
}

$tables = @($manifest.exported_tables)
if ($tables.Count -eq 0 -or $tables.Count -ne [int]$manifest.exported_table_count) {
    $errors.Add('WPA export table count is invalid.')
}
foreach ($table in $tables) {
    $path = Resolve-SafeChildPath -Root $exportRoot `
        -RelativePath ([string]$table.relative_path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("WPA export table is missing: $($table.relative_path)")
        continue
    }
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($item.Length -le 0 -or $item.Length -ne [long]$table.size_bytes `
        -or $hash -ne [string]$table.sha256) {
        $errors.Add("WPA export table changed: $($table.relative_path)")
    }
}

if ($errors.Count -gt 0) {
    throw ($errors -join [Environment]::NewLine)
}
Write-Host 'Native performance WPA export is internally consistent.'
Write-Host 'Analysis status: pending_review; release gate remains blocked.'
