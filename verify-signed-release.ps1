#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Independently verify hashes and Authenticode signatures in a Windows release.
#>
param(
    [Parameter(Mandatory=$true)] [string] $ReleaseDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [string] $SignToolPath,
    [string] $ExpectedCertificateThumbprint,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

function Resolve-SignTool([string] $requestedPath) {
    if (-not [string]::IsNullOrWhiteSpace($requestedPath)) {
        $resolved = [IO.Path]::GetFullPath($requestedPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "SignTool was not found: $resolved" }
        return $resolved
    }
    $sdkRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $tools = @(Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { try { [Version]$_.Directory.Parent.Name } catch { [Version]'0.0' } } -Descending)
    if ($tools.Count -eq 0) { throw 'SignTool x64 was not found. Install the Windows SDK signing tools.' }
    return $tools[0].FullName
}

function Get-ProductSigningTargets([string] $packageRoot) {
    $targets = @(
        (Join-Path $packageRoot 'LongBetterWindows.Host.exe'),
        (Join-Path $packageRoot 'LongBetterWindows.Host.dll')
    )
    $pluginsRoot = Join-Path $packageRoot 'Plugins'
    foreach ($manifestPath in @(Get-ChildItem -LiteralPath $pluginsRoot -Filter manifest.json -File -Recurse)) {
        $plugin = Get-Content -LiteralPath $manifestPath.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $entryPoint = [string]$plugin.entry_point
        if ([IO.Path]::GetExtension($entryPoint) -notin @('.dll','.exe')) { continue }
        $candidate = [IO.Path]::GetFullPath((Join-Path $manifestPath.Directory.FullName $entryPoint))
        $packagePrefix = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\') + '\'
        if (-not $candidate.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Plugin signing target escapes package root: $entryPoint"
        }
        $targets += $candidate
    }
    $unique = @($targets | Sort-Object -Unique)
    foreach ($target in $unique) {
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Product signing target is missing: $target" }
    }
    return $unique
}

function Resolve-Within([string] $root, [string] $relativePath, [string] $description) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "$description must be a non-rooted relative path."
    }
    $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $candidate = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$description escapes release root: $relativePath"
    }
    return $candidate
}

$releaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Release manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([bool]$manifest.source_dirty) { throw 'Signed release verification rejects source_dirty=true.' }
if (-not [bool]$manifest.signed -or -not [bool]$manifest.release_eligible) {
    throw 'Release manifest is not marked signed and release-eligible.'
}
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$manifestCommit = ([string]$manifest.commit).Trim().ToLowerInvariant()
$signingCommit = ([string]$manifest.signing.source_commit).Trim().ToLowerInvariant()
if ($manifestCommit -ne $expectedCommit -or $signingCommit -ne $expectedCommit) {
    throw 'Signed release source commit does not match ExpectedSourceCommit.'
}
$manifestThumbprint = ([string]$manifest.signing.certificate_thumbprint).Replace(' ','').ToUpperInvariant()
$expectedThumbprint = if ([string]::IsNullOrWhiteSpace($ExpectedCertificateThumbprint)) {
    $manifestThumbprint
} else { $ExpectedCertificateThumbprint.Replace(' ','').ToUpperInvariant() }
if ([string]::IsNullOrWhiteSpace($expectedThumbprint) -or $expectedThumbprint -ne $manifestThumbprint) {
    throw 'Expected signing certificate thumbprint does not match the release manifest.'
}
$signTool = Resolve-SignTool $SignToolPath
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("long-sign-verify-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$results = @()
try {
    foreach ($package in @($manifest.packages)) {
        $zipPath = Resolve-Within $releaseRoot ([string]$package.file) 'Package ZIP'
        if ([IO.Path]::GetExtension($zipPath) -ne '.zip') { throw 'Package file must use the .zip extension.' }
        if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw "Release ZIP is missing: $zipPath" }
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($zipHash -ne ([string]$package.sha256).ToLowerInvariant()) { throw "Release ZIP hash mismatch: $($package.file)" }
        $extractRoot = Resolve-Within $tempRoot ([string]$package.kind) 'Package extraction directory'
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot
        $targets = @(Get-ProductSigningTargets $extractRoot)
        if ($targets.Count -ne [int]$package.signed_files) {
            throw "Signed file count mismatch for $($package.kind). Expected=$($package.signed_files), actual=$($targets.Count)."
        }
        foreach ($target in $targets) {
            $signature = Get-AuthenticodeSignature -LiteralPath $target
            $thumbprint = if ($null -eq $signature.SignerCertificate) {
                ''
            } else { $signature.SignerCertificate.Thumbprint.Replace(' ','').ToUpperInvariant() }
            if ($signature.Status -ne 'Valid' -or $thumbprint -ne $expectedThumbprint) {
                throw "Authenticode signature is invalid or has the wrong signer: $target"
            }
            & $signTool verify /pa /all /tw /q $target
            if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed with exit code $LASTEXITCODE`: $target" }
        }
        $results += [ordered]@{
            file = [string]$package.file
            kind = [string]$package.kind
            sha256 = $zipHash
            signed_files = $targets.Count
        }
    }
    foreach ($installer in @($manifest.installers)) {
        $installerPath = Resolve-Within $releaseRoot ([string]$installer.file) 'Installer'
        if ([IO.Path]::GetExtension($installerPath) -ne '.exe' `
            -or -not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            throw "Signed installer is missing or invalid: $installerPath"
        }
        $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($installerHash -ne ([string]$installer.sha256).ToLowerInvariant() `
            -or [long](Get-Item -LiteralPath $installerPath).Length -ne [long]$installer.bytes `
            -or -not [bool]$installer.signed) {
            throw "Signed installer identity mismatch: $($installer.file)"
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
        $installerThumbprint = if ($null -eq $signature.SignerCertificate) {
            ''
        } else { $signature.SignerCertificate.Thumbprint.Replace(' ','').ToUpperInvariant() }
        if ($signature.Status -ne 'Valid' -or $installerThumbprint -ne $expectedThumbprint) {
            throw "Installer Authenticode signature is invalid or has the wrong signer: $installerPath"
        }
        & $signTool verify /pa /all /tw /q $installerPath
        if ($LASTEXITCODE -ne 0) {
            throw "Installer SignTool verification failed with exit code $LASTEXITCODE`: $installerPath"
        }
        $results += [ordered]@{
            file = [string]$installer.file
            kind = 'installer'
            sha256 = $installerHash
            signed_files = 1
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}

$summary = [ordered]@{
    schema_version = 1
    verified_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'verified_windows_authenticode_release'
    version = [string]$manifest.version
    source_commit = $expectedCommit
    certificate_thumbprint = $expectedThumbprint
    timestamp_url = [string]$manifest.signing.timestamp_url
    passed = $true
    packages = $results
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    Write-Output "Code-signing verification report: $resolvedOutput"
}
Write-Output 'Signed Windows release verified.'
