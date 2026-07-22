#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Sign a clean LongBetterWindows candidate through the Windows certificate store.
.DESCRIPTION
  Never accepts PFX passwords. Use a protected CurrentUser/LocalMachine certificate store,
  hardware token, or provider-backed private key. The input candidate is never modified.
#>
param(
    [Parameter(Mandatory=$true)] [string] $InputReleaseDirectory,
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [Parameter(Mandatory=$true)] [string] $CertificateThumbprint,
    [Parameter(Mandatory=$true)] [string] $ExpectedSubject,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [uri] $TimestampUrl,
    [ValidateSet('CurrentUser','LocalMachine')] [string] $CertificateStoreLocation = 'CurrentUser',
    [string] $SignToolPath,
    [switch] $ConfirmSign
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmSign) { throw 'ConfirmSign is required before invoking a protected code-signing key.' }
if ($TimestampUrl.Scheme -ne 'https') { throw 'TimestampUrl must use HTTPS.' }
if ([string]::IsNullOrWhiteSpace($ExpectedSubject)) { throw 'ExpectedSubject must not be empty.' }
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$inputRoot = [IO.Path]::GetFullPath($InputReleaseDirectory).TrimEnd('\')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($inputRoot -eq $outputRoot) { throw 'Input and output release directories must differ.' }
if (Test-Path -LiteralPath $outputRoot) { throw "Signed release output already exists: $outputRoot" }
$manifestPath = Join-Path $inputRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Release manifest was not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([bool]$manifest.source_dirty) { throw 'Code signing requires a candidate rebuilt from a clean source commit.' }
if ([bool]$manifest.signed) { throw 'Input release is already marked signed.' }
$manifestCommit = ([string]$manifest.commit).Trim().ToLowerInvariant()
if ($manifestCommit -notmatch '^[0-9a-f]{40}$' -or $manifestCommit -ne $expectedCommit) {
    throw 'Candidate source commit does not match ExpectedSourceCommit.'
}
if (@(Get-ChildItem -LiteralPath $inputRoot -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Code signing input must not contain reparse points.'
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

$thumbprint = $CertificateThumbprint.Replace(' ','').ToUpperInvariant()
$storePath = "Cert:\$CertificateStoreLocation\My\$thumbprint"
$certificate = Get-Item -LiteralPath $storePath -ErrorAction SilentlyContinue
if ($null -eq $certificate) { throw "Code-signing certificate was not found in $CertificateStoreLocation\My." }
if (-not $certificate.HasPrivateKey) { throw 'The selected code-signing certificate has no accessible private key.' }
if ($certificate.NotBefore -gt [DateTime]::Now -or $certificate.NotAfter -le [DateTime]::Now.AddDays(7)) {
    throw 'The selected code-signing certificate is not currently valid for at least seven more days.'
}
if ($certificate.Subject -notlike "*$ExpectedSubject*") { throw 'Certificate subject does not match ExpectedSubject.' }
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$hasCodeSigningEku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
    ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq $codeSigningOid }).Count -gt 0
if (-not $hasCodeSigningEku) { throw 'Certificate does not include the Code Signing enhanced key usage.' }

$verifyScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'verify-signed-release.ps1'
$signToolResolver = & {
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) { return [IO.Path]::GetFullPath($SignToolPath) }
    $sdkRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $tools = @(Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object { try { [Version]$_.Directory.Parent.Name } catch { [Version]'0.0' } } -Descending)
    if ($tools.Count -eq 0) { throw 'SignTool x64 was not found. Install the Windows SDK signing tools.' }
    return $tools[0].FullName
}
if (-not (Test-Path -LiteralPath $signToolResolver -PathType Leaf)) { throw "SignTool was not found: $signToolResolver" }

function Get-ProductSigningTargets([string] $packageRoot) {
    $targets = @((Join-Path $packageRoot 'LongBetterWindows.Host.exe'), (Join-Path $packageRoot 'LongBetterWindows.Host.dll'))
    foreach ($manifestFile in @(Get-ChildItem -LiteralPath (Join-Path $packageRoot 'Plugins') -Filter manifest.json -File -Recurse)) {
        $plugin = Get-Content -LiteralPath $manifestFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $entryPoint = [string]$plugin.entry_point
        if ([IO.Path]::GetExtension($entryPoint) -notin @('.dll','.exe')) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $manifestFile.Directory.FullName $entryPoint))
        $prefix = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\') + '\'
        if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Plugin signing target escapes package root: $entryPoint" }
        $targets += $target
    }
    return @($targets | Sort-Object -Unique)
}

$parent = Split-Path -Parent $outputRoot
if ([string]::IsNullOrWhiteSpace($parent)) { throw 'OutputDirectory must have a parent directory.' }
[IO.Directory]::CreateDirectory($parent) | Out-Null
$stagingRoot = Join-Path $parent ('.long-signing-' + [Guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $inputRoot -Destination $stagingRoot -Recurse
    $signedPackages = @()
    foreach ($package in @($manifest.packages)) {
        $packageRoot = Resolve-Within $stagingRoot ([string]$package.kind) 'Package directory'
        if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
            throw "Package directory is missing: $packageRoot"
        }
        $targets = @(Get-ProductSigningTargets $packageRoot)
        foreach ($target in $targets) {
            $arguments = @('sign','/sha1',$thumbprint,'/s','My','/fd','SHA256','/tr',$TimestampUrl.AbsoluteUri,'/td','SHA256','/d','Long窗口·全能助手')
            if ($CertificateStoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
            $arguments += $target
            & $signToolResolver @arguments
            if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed with exit code $LASTEXITCODE`: $target" }
            & $signToolResolver verify /pa /all /tw /q $target
            if ($LASTEXITCODE -ne 0) { throw "SignTool post-sign verification failed with exit code $LASTEXITCODE`: $target" }
        }
        $zipPath = Resolve-Within $stagingRoot ([string]$package.file) 'Package ZIP'
        if ([IO.Path]::GetExtension($zipPath) -ne '.zip') { throw 'Package file must use the .zip extension.' }
        Remove-Item -LiteralPath $zipPath -Force
        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $package.sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $package.bytes = (Get-Item -LiteralPath $zipPath).Length
        $package | Add-Member -NotePropertyName signed_files -NotePropertyValue $targets.Count -Force
        $signedPackages += $package
    }
    $manifest.packages = $signedPackages
    $manifest.distribution_channel = 'signed'
    $manifest.publisher_identity = 'authenticode'
    $manifest.security_notice = $null
    $manifest.signed = $true
    $manifest.release_eligible = $true
    $manifest | Add-Member -NotePropertyName signing -NotePropertyValue ([ordered]@{
        signed_at = [DateTimeOffset]::UtcNow.ToString('O')
        source_commit = $expectedCommit
        certificate_thumbprint = $thumbprint
        certificate_subject = $certificate.Subject
        certificate_not_after = $certificate.NotAfter.ToUniversalTime().ToString('O')
        digest_algorithm = 'SHA256'
        timestamp_digest_algorithm = 'SHA256'
        timestamp_url = $TimestampUrl.AbsoluteUri
        certificate_store = "$CertificateStoreLocation\\My"
        signtool_version = [Diagnostics.FileVersionInfo]::GetVersionInfo($signToolResolver).FileVersion
    }) -Force
    $manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $stagingRoot 'release-manifest.json') -Encoding UTF8
    @($signedPackages | ForEach-Object { "$($_.sha256)  $($_.file)" }) |
        Set-Content -LiteralPath (Join-Path $stagingRoot 'SHA256SUMS.txt') -Encoding UTF8

    & $verifyScript `
        -ReleaseDirectory $stagingRoot `
        -SignToolPath $signToolResolver `
        -ExpectedCertificateThumbprint $thumbprint `
        -ExpectedSourceCommit $expectedCommit
    Move-Item -LiteralPath $stagingRoot -Destination $outputRoot
    Write-Output "Signed release created: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
