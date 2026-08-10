#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [string] $ExportDirectory,
    [Parameter(Mandatory=$true)] [string] $Reviewer,
    [Parameter(Mandatory=$true)] [string] $Notes,
    [Parameter(Mandatory=$true)] [string[]] $CpuEvidenceFiles,
    [Parameter(Mandatory=$true)] [string[]] $CompositionEvidenceFiles,
    [Parameter(Mandatory=$true)] [string] $ExpectedCommit,
    [switch] $ConfirmCpuSampledReviewed,
    [switch] $ConfirmDesktopCompositionReviewed,
    [switch] $ConfirmTimelineCorrelated,
    [switch] $ConfirmNoUnresolvedProductHotspot,
    [switch] $ConfirmPassed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Get-EvidenceEntry([string] $PathValue, [string] $Category, [string] $Root) {
    $path = Resolve-RepositoryPath $PathValue
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Analysis evidence must remain under the capture directory: $path"
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Analysis evidence file was not found: $path"
    }
    if ([IO.Path]::GetExtension($path).ToLowerInvariant() `
        -notin @('.csv', '.xml', '.png', '.jpg', '.jpeg')) {
        throw "Analysis evidence must be a WPA table or screenshot: $path"
    }
    $item = Get-Item -LiteralPath $path
    if ($item.Length -le 0) {
        throw "Analysis evidence is empty: $path"
    }
    return [ordered]@{
        category = $Category
        relative_path = $path.Substring($prefix.Length).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        size_bytes = $item.Length
    }
}

if (-not $ConfirmPassed -or -not $ConfirmCpuSampledReviewed `
    -or -not $ConfirmDesktopCompositionReviewed `
    -or -not $ConfirmTimelineCorrelated `
    -or -not $ConfirmNoUnresolvedProductHotspot) {
    throw 'Native performance analysis requires every review confirmation and -ConfirmPassed.'
}
if ([string]::IsNullOrWhiteSpace($Reviewer) -or [string]::IsNullOrWhiteSpace($Notes)) {
    throw 'Reviewer and Notes are required.'
}
$sourceCommit = $ExpectedCommit.Trim().ToLowerInvariant()
if ($sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedCommit must be a full 40-character Git commit.'
}
$evidenceRoot = Resolve-RepositoryPath $EvidenceDirectory
$exportRoot = if ([string]::IsNullOrWhiteSpace($ExportDirectory)) {
    Join-Path $evidenceRoot 'wpa-export'
}
else {
    Resolve-RepositoryPath $ExportDirectory
}
& (Join-Path $PSScriptRoot 'verify-native-performance-export.ps1') `
    -EvidenceDirectory $evidenceRoot -ExportDirectory $exportRoot `
    -ExpectedCommit $sourceCommit

$entries = @(
    $CpuEvidenceFiles | ForEach-Object {
        Get-EvidenceEntry -PathValue $_ -Category 'cpu_sampled' -Root $evidenceRoot
    }
    $CompositionEvidenceFiles | ForEach-Object {
        Get-EvidenceEntry -PathValue $_ -Category 'desktop_composition' -Root $evidenceRoot
    }
)
$uniquePaths = @($entries.relative_path | Sort-Object -Unique)
if ($CpuEvidenceFiles.Count -eq 0 -or $CompositionEvidenceFiles.Count -eq 0 `
    -or $uniquePaths.Count -ne $entries.Count) {
    throw 'CPU and Desktop Composition require separate, non-empty evidence files.'
}

$rawManifestPath = Join-Path $evidenceRoot 'native-performance-evidence.json'
$rawManifest = Get-Content -LiteralPath $rawManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$exportManifestPath = Join-Path $exportRoot 'native-performance-export.json'
$evidencePrefix = $evidenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$reportPath = Join-Path $evidenceRoot 'native-performance-analysis.json'
$report = [ordered]@{
    schema_version = 1
    classification = 'native_performance_analysis'
    reviewed_at = [DateTimeOffset]::UtcNow.ToString('O')
    source_commit = $sourceCommit
    reviewer = $Reviewer.Trim()
    notes = $Notes.Trim()
    raw_manifest_sha256 = (Get-FileHash -LiteralPath $rawManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    trace_sha256 = ([string]$rawManifest.trace_sha256).ToLowerInvariant()
    export_manifest_file = $exportManifestPath.Substring($evidencePrefix.Length).Replace('\', '/')
    export_manifest_sha256 = (Get-FileHash -LiteralPath $exportManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    confirmations = [ordered]@{
        cpu_sampled_reviewed = $true
        desktop_composition_reviewed = $true
        timeline_correlated = $true
        no_unresolved_product_hotspot = $true
    }
    evidence_files = $entries
    analysis_status = 'reviewed_pass'
    passed = $true
    release_gate_passed = $false
}
Write-NewJsonFileAtomically -Value $report -Path $reportPath -Depth 10 `
    -Label 'Native performance analysis report'
Write-Host "Native performance analysis report created: $reportPath"
Write-Host 'Independent final approval is still required; release gate remains blocked.'
