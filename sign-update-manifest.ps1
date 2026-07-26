#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Creates and signs update-manifest.json for a built release directory.
#>
param(
    [Parameter(Mandatory)]
    [string] $ReleaseDirectory,
    [Parameter(Mandatory)]
    [string] $Tag,
    [string] $Repository = 'Longyuyeee/Long_BetterWindows',
    [string] $PrivateKeyPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
    $PrivateKeyPath = Join-Path $repoRoot 'local-secrets\update-signing\update-signing.private.key'
}

$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
    throw "Release directory does not exist: $releaseRoot"
}
if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
    throw "Update signing key does not exist. Run .\new-update-signing-key.ps1 first."
}
if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$') {
    throw "Tag must be a semantic version prefixed with v: $Tag"
}
$version = $Matches.version
if ($Repository -notmatch '^(?<owner>[A-Za-z0-9_.-]+)/(?<name>[A-Za-z0-9_.-]+)$') {
    throw "Repository must use owner/name form."
}

$commit = (git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the source commit.'
}

$packages = @(
    Get-ChildItem -LiteralPath $releaseRoot -Filter "LongBetterWindows-v$version-win-x64-*.zip" -File |
        Sort-Object Name |
        ForEach-Object {
            $kind = if ($_.Name -like '*-self-contained.zip') {
                'self-contained'
            } elseif ($_.Name -like '*-framework-dependent.zip') {
                'framework-dependent'
            } else {
                throw "Unexpected update package name: $($_.Name)"
            }
            [ordered]@{
                kind = $kind
                file = $_.Name
                url = "https://github.com/$Repository/releases/download/$Tag/$($_.Name)"
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                bytes = $_.Length
            }
        }
)
if ($packages.Count -eq 0) {
    throw "No release ZIP packages found in $releaseRoot"
}

$manifest = [ordered]@{
    schema_version = 1
    product = 'Long Assistant'
    version = $version
    channel = if ($version -match '-') { 'prerelease' } else { 'stable' }
    published_at = [DateTimeOffset]::UtcNow.ToString('o')
    source_commit = $commit
    release_page = "https://github.com/$Repository/releases/tag/$Tag"
    packages = $packages
}

$manifestPath = Join-Path $releaseRoot 'update-manifest.json'
$signaturePath = Join-Path $releaseRoot 'update-manifest.sig'
$json = $manifest | ConvertTo-Json -Depth 5
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
[IO.File]::WriteAllBytes($manifestPath, $bytes)

$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.FromXmlString([IO.File]::ReadAllText($PrivateKeyPath))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $signature = $rsa.SignData($bytes, $sha256)
    }
    finally {
        $sha256.Dispose()
    }
    [IO.File]::WriteAllText(
        $signaturePath,
        [Convert]::ToBase64String($signature),
        [Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Dispose()
}

Write-Host "Signed update manifest: $manifestPath"
