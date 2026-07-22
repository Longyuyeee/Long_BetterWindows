#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify a downloaded release package and capture its Windows internet-origin evidence.
.DESCRIPTION
  This script does not extract or run the package. SmartScreen, antivirus, extraction, and
  first-launch observations remain explicit human-review items.
#>
param(
    [Parameter(Mandatory=$true)] [string] $DownloadedPackage,
    [Parameter(Mandatory=$true)] [string] $ReleaseManifest,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [Parameter(Mandatory=$true)] [string] $OutputPath,
    [string[]] $AllowedDownloadHosts = @(
        'github.com',
        'objects.githubusercontent.com',
        'release-assets.githubusercontent.com'
    )
)

$ErrorActionPreference = 'Stop'

function Get-ZoneTransferFields([string] $filePath) {
    $zoneStream = Get-Item -LiteralPath $filePath -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue
    if ($null -eq $zoneStream) {
        throw 'Downloaded package does not contain a Zone.Identifier stream.'
    }

    $raw = Get-Content -LiteralPath $filePath -Stream 'Zone.Identifier' -Raw -Encoding UTF8
    $fields = @{}
    foreach ($line in @($raw -split "`r?`n")) {
        if ($line -match '^([^=]+)=(.*)$') {
            $fields[$matches[1].Trim()] = $matches[2].Trim().Trim([char]0)
        }
    }
    return [ordered]@{
        raw = $raw
        zone_id = [string]$fields['ZoneId']
        host_url = [string]$fields['HostUrl']
        referrer_url = [string]$fields['ReferrerUrl']
    }
}

function ConvertTo-SanitizedUri([string] $value, [string] $fieldName) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    $uri = $null
    if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') {
        throw "$fieldName must be an absolute HTTPS URL."
    }
    return [ordered]@{
        scheme = $uri.Scheme
        host = $uri.IdnHost.ToLowerInvariant()
        path = $uri.AbsolutePath
    }
}

$packagePath = [IO.Path]::GetFullPath($DownloadedPackage)
$manifestPath = [IO.Path]::GetFullPath($ReleaseManifest)
$resolvedOutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Downloaded package was not found: $packagePath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Release manifest was not found: $manifestPath"
}
if (Test-Path -LiteralPath $resolvedOutputPath) {
    throw "Download evidence output already exists: $resolvedOutputPath"
}
if ($AllowedDownloadHosts.Count -eq 0) { throw 'At least one allowed download host is required.' }
$allowedHosts = @($AllowedDownloadHosts | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ } | Sort-Object -Unique)

$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}

$release = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (([string]$release.commit).Trim().ToLowerInvariant() -ne $expectedCommit) {
    throw 'Release manifest source commit does not match ExpectedSourceCommit.'
}
if ([bool]$release.source_dirty -or -not [bool]$release.release_eligible) {
    throw 'Release manifest is not a clean, release-eligible candidate.'
}
if ([string]$release.distribution_channel -ne $ExpectedDistributionChannel) {
    throw 'Release manifest distribution channel does not match ExpectedDistributionChannel.'
}
if (($ExpectedDistributionChannel -eq 'signed' -and -not [bool]$release.signed) -or
    ($ExpectedDistributionChannel -eq 'unsigned' -and [bool]$release.signed)) {
    throw 'Release signature state does not match its distribution channel.'
}

$packageName = [IO.Path]::GetFileName($packagePath)
$packages = @($release.packages | Where-Object { [string]$_.file -eq $packageName })
if ($packages.Count -ne 1) {
    throw "Release manifest does not identify exactly one package named $packageName."
}
$package = $packages[0]
$packageInfo = Get-Item -LiteralPath $packagePath
$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne ([string]$package.sha256).ToLowerInvariant()) {
    throw 'Downloaded package hash does not match the release manifest.'
}
if ([long]$packageInfo.Length -ne [long]$package.bytes) {
    throw 'Downloaded package size does not match the release manifest.'
}

$zone = Get-ZoneTransferFields $packagePath
if ($zone.zone_id -ne '3') {
    throw "Downloaded package must retain Internet ZoneId=3; actual ZoneId=$($zone.zone_id)."
}
$hostOrigin = ConvertTo-SanitizedUri $zone.host_url 'Zone.Identifier HostUrl'
if ($null -eq $hostOrigin -or $hostOrigin.host -notin $allowedHosts) {
    throw "Zone.Identifier HostUrl is not an allowed download host: $($hostOrigin.host)"
}
$referrer = ConvertTo-SanitizedUri $zone.referrer_url 'Zone.Identifier ReferrerUrl'
if ($null -ne $referrer -and $referrer.host -notin $allowedHosts) {
    throw "Zone.Identifier ReferrerUrl is not an allowed download host: $($referrer.host)"
}

$zoneBytes = [Text.Encoding]::UTF8.GetBytes([string]$zone.raw)
$zoneHash = [BitConverter]::ToString(([Security.Cryptography.SHA256]::Create()).ComputeHash($zoneBytes)).Replace('-', '').ToLowerInvariant()
$evidence = [ordered]@{
    schema_version = 1
    captured_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'verified_release_download_provenance'
    passed = $true
    release = [ordered]@{
        version = [string]$release.version
        source_commit = $expectedCommit
        distribution_channel = $ExpectedDistributionChannel
        signed = [bool]$release.signed
        release_eligible = [bool]$release.release_eligible
    }
    package = [ordered]@{
        file = $packageName
        kind = [string]$package.kind
        bytes = [long]$packageInfo.Length
        sha256 = $actualHash
    }
    windows_origin = [ordered]@{
        zone_id = 3
        host = $hostOrigin
        referrer = $referrer
        zone_identifier_sha256 = $zoneHash
        query_parameters_recorded = $false
    }
    human_review = [ordered]@{
        status = 'pending'
        checklist = [ordered]@{
            extraction_completed = $false
            extracted_executable_origin_checked = $false
            smartscreen_observed = $false
            antivirus_observed = $false
            first_launch_observed = $false
        }
    }
}

$outputParent = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
    [IO.Directory]::CreateDirectory($outputParent) | Out-Null
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutputPath -Encoding UTF8
Write-Output 'Downloaded release identity and Windows internet-origin evidence verified.'
Write-Output 'Interactive extraction, SmartScreen, antivirus, and first-launch review remain pending.'
Write-Output "Evidence: $resolvedOutputPath"
