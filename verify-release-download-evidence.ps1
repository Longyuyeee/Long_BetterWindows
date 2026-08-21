#!/usr/bin/env pwsh
<# .SYNOPSIS Verify immutable automated release-download provenance evidence. #>
param(
    [Parameter(Mandatory=$true)] [string] $EvidencePath,
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
$resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if (-not (Test-Path -LiteralPath $resolvedEvidencePath -PathType Leaf)) {
    throw "Release-download evidence was not found: $resolvedEvidencePath"
}

$evidence = Get-Content -LiteralPath $resolvedEvidencePath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ([int]$evidence.schema_version -ne 2 -or
    $evidence.classification -ne 'automated_release_download_provenance' -or
    -not [bool]$evidence.passed) {
    throw 'Release-download provenance capture is not passing schema version 2.'
}
if ([string]$evidence.release.source_commit -ne $expectedCommit) {
    throw 'Release-download gate source commit does not match ExpectedSourceCommit.'
}
if ([string]$evidence.release.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Release-download gate distribution channel does not match ExpectedDistributionChannel.'
}
if (-not [bool]$evidence.release.release_eligible -or
    ($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$evidence.release.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$evidence.release.signed)) {
    throw 'Release-download evidence does not match the expected eligible distribution channel.'
}
if ([string]::IsNullOrWhiteSpace([string]$evidence.package.file) -or
    [long]$evidence.package.bytes -lt 1 -or
    [string]$evidence.package.sha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Release-download package identity is incomplete.'
}
if ([int]$evidence.windows_origin.zone_id -ne 3 -or
    [string]$evidence.windows_origin.host.scheme -ne 'https' -or
    [string]::IsNullOrWhiteSpace([string]$evidence.windows_origin.host.host) -or
    [string]$evidence.windows_origin.zone_identifier_sha256 -notmatch '^[0-9a-f]{64}$' -or
    [bool]$evidence.windows_origin.query_parameters_recorded) {
    throw 'Release-download evidence does not contain a sanitized HTTPS Internet Zone origin.'
}

$actualEvidenceHash = (Get-FileHash -LiteralPath $resolvedEvidencePath -Algorithm SHA256).
    Hash.ToLowerInvariant()
$summary = [ordered]@{
    schema_version = 4
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'automated_release_download_gate'
    passed = $true
    source_commit = $expectedCommit
    distribution_channel = $ExpectedDistributionChannel
    version = [string]$evidence.release.version
    package_file = [string]$evidence.package.file
    package_bytes = [long]$evidence.package.bytes
    package_sha256 = [string]$evidence.package.sha256
    download_host = [string]$evidence.windows_origin.host.host
    evidence = [ordered]@{
        file = [IO.Path]::GetFileName($resolvedEvidencePath)
        sha256 = $actualEvidenceHash
    }
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
    $outputParent = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::Equals(
        [IO.Path]::GetFullPath((Split-Path -Parent $resolvedEvidencePath)).TrimEnd('\'),
        [IO.Path]::GetFullPath($outputParent).TrimEnd('\'),
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release-download summary and evidence must share one directory.'
    }
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $resolvedOutputPath `
        -Depth 5 `
        -Label 'Release-download gate summary'
    Write-Output "Release-download gate summary: $resolvedOutputPath"
}
Write-Output 'Automated release-download gate verified.'
