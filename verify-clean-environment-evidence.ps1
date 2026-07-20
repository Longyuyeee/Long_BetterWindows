#!/usr/bin/env pwsh
<# .SYNOPSIS Verify approved, hash-locked clean Windows release evidence. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'clean-environment-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'clean_windows_release_evidence') { throw 'Unexpected evidence classification.' }
if (-not [bool]$manifest.environment.operator_asserted_clean_user -or -not [bool]$manifest.environment.interactive) {
    throw 'Evidence was not captured in an asserted clean interactive Windows user environment.'
}
if (-not [bool]$manifest.automated_checks.passed -or $manifest.human_review.status -ne 'approved') {
    throw 'Clean-environment evidence is not fully approved.'
}
if (-not [bool]$manifest.release.signed -or -not [bool]$manifest.release.release_eligible) {
    throw 'Formal clean-environment release evidence requires a signed, release-eligible package.'
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.human_review.reviewer)) { throw 'Human reviewer is missing.' }
foreach ($property in $manifest.human_review.checklist.psobject.Properties) {
    if (-not [bool]$property.Value) { throw "Manual lifecycle checklist is incomplete: $($property.Name)" }
}
foreach ($entry in @(
    $manifest.release.release_manifest,
    $manifest.automated_checks.desktop_ui_report,
    $manifest.automated_checks.desktop_ui_log,
    $manifest.automated_checks.command_log
)) {
    $path = Join-Path $root ([string]$entry.file)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Evidence file is missing: $path" }
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
        throw "Clean-environment evidence hash mismatch: $($entry.file)"
    }
}

$summary = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_clean_windows_release_gate'
    passed = $true
    version = [string]$manifest.release.version
    package_sha256 = [string]$manifest.release.package_sha256
    environment_label = [string]$manifest.environment.label
    reviewer = [string]$manifest.human_review.reviewer
    evidence_manifest_sha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    Write-Output "Clean-environment gate summary: $resolvedOutput"
}
Write-Output 'Clean Windows release gate verified.'
