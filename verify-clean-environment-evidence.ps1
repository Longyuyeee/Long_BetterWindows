#!/usr/bin/env pwsh
<# .SYNOPSIS Verify automated, hash-locked clean Windows release evidence. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
$manifestPath = Join-Path $root 'clean-environment-evidence.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int]$manifest.schema_version -ne 2 -or
    $manifest.classification -ne 'automated_clean_windows_release_evidence') {
    throw 'Unexpected clean-environment evidence contract.'
}
if ([string]$manifest.release.source_commit -ne $expectedCommit) {
    throw 'Clean-environment evidence source commit does not match ExpectedSourceCommit.'
}
if ([string]$manifest.release.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Clean-environment evidence distribution channel does not match ExpectedDistributionChannel.'
}
if (-not [bool]$manifest.environment.operator_asserted_clean_user -or -not [bool]$manifest.environment.interactive) {
    throw 'Evidence was not captured in an asserted clean interactive Windows user environment.'
}
if (-not [bool]$manifest.automated_checks.passed) {
    throw 'Clean-environment automated checks did not pass.'
}
if (-not [bool]$manifest.release.release_eligible -or
    ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$manifest.release.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$manifest.release.signed)) {
    throw 'Clean-environment release state does not match the expected eligible distribution channel.'
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
    schema_version = 3
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'automated_clean_windows_release_gate'
    passed = $true
    version = [string]$manifest.release.version
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    signed = [bool]$manifest.release.signed
    package_sha256 = [string]$manifest.release.package_sha256
    environment_label = [string]$manifest.environment.label
    evidence_manifest = [ordered]@{
        file = 'clean-environment-evidence.json'
        sha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::Equals(
        [IO.Path]::GetFullPath($root).TrimEnd('\'),
        [IO.Path]::GetFullPath($parent).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Clean-environment summary and evidence manifest must share one directory.'
    }
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $resolvedOutput `
        -Depth 5 `
        -Label 'Clean-environment gate summary'
    Write-Output "Clean-environment gate summary: $resolvedOutput"
}
Write-Output 'Clean Windows release gate verified.'
