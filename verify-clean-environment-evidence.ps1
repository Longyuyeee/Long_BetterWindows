#!/usr/bin/env pwsh
<# .SYNOPSIS Verify approved, hash-locked clean Windows release evidence. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'clean-environment-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.classification -ne 'clean_windows_release_evidence') { throw 'Unexpected evidence classification.' }
if ([string]$manifest.release.source_commit -ne $expectedCommit) {
    throw 'Clean-environment evidence source commit does not match ExpectedSourceCommit.'
}
if ([string]$manifest.release.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Clean-environment evidence distribution channel does not match ExpectedDistributionChannel.'
}
if (-not [bool]$manifest.environment.operator_asserted_clean_user -or -not [bool]$manifest.environment.interactive) {
    throw 'Evidence was not captured in an asserted clean interactive Windows user environment.'
}
if (-not [bool]$manifest.automated_checks.passed -or $manifest.human_review.status -ne 'approved') {
    throw 'Clean-environment evidence is not fully approved.'
}
if (-not [bool]$manifest.release.release_eligible -or
    ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$manifest.release.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$manifest.release.signed)) {
    throw 'Clean-environment release state does not match the expected eligible distribution channel.'
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
$capturedRelease = Get-Content -LiteralPath (Join-Path $root ([string]$manifest.release.release_manifest.file)) `
    -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$capturedRelease.commit -ne $expectedCommit) {
    throw 'Captured release manifest source commit does not match ExpectedSourceCommit.'
}
if ([string]$capturedRelease.distribution_channel -ne $ExpectedDistributionChannel -or
    -not [bool]$capturedRelease.release_eligible -or
    ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$capturedRelease.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$capturedRelease.signed)) {
    throw 'Captured release manifest does not match the expected eligible distribution channel.'
}
$capturedPackage = @($capturedRelease.packages | Where-Object { $_.file -eq [string]$manifest.release.package_file })
if ($capturedPackage.Count -ne 1 -or [string]$capturedPackage[0].sha256 -ne [string]$manifest.release.package_sha256) {
    throw 'Clean-environment package identity does not match the captured release manifest.'
}

$summary = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'approved_clean_windows_release_gate'
    passed = $true
    version = [string]$manifest.release.version
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    signed = [bool]$manifest.release.signed
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
