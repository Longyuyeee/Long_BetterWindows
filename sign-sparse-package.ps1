#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $InputPackage,
    [Parameter(Mandatory=$true)] [string] $OutputPackage,
    [Parameter(Mandatory=$true)] [string] $CertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string] $CertificateStoreLocation = 'CurrentUser',
    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$inputPath = [IO.Path]::GetFullPath($InputPackage)
$outputPath = [IO.Path]::GetFullPath($OutputPackage)
if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    throw "Sparse package was not found: $inputPath"
}
if (Test-Path -LiteralPath $outputPath) {
    throw "Signed output already exists: $outputPath"
}
if ([IO.Path]::GetExtension($inputPath) -notin @('.msix', '.appx')) {
    throw 'Input package must use the .msix or .appx extension.'
}
if ($inputPath -eq $outputPath) {
    throw 'Input and output packages must be different files.'
}
if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $timestamp = [Uri]$TimestampUrl
    if (-not $timestamp.IsAbsoluteUri -or $timestamp.Scheme -ne 'https') {
        throw 'TimestampUrl must be an absolute HTTPS URL.'
    }
}

$thumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
if ($thumbprint -notmatch '^[0-9A-F]{40,128}$') {
    throw 'Certificate thumbprint is invalid.'
}
$certificatePath = "Cert:\$CertificateStoreLocation\My\$thumbprint"
$certificate = Get-Item -LiteralPath $certificatePath -ErrorAction Stop
if (-not $certificate.HasPrivateKey) {
    throw 'The selected certificate does not have an accessible private key.'
}
if ($certificate.NotBefore -gt [DateTime]::Now -or
    $certificate.NotAfter -le [DateTime]::Now) {
    throw 'The selected certificate is not currently valid.'
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$allowsCodeSigning = $certificate.Extensions |
    Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
    ForEach-Object { $_.EnhancedKeyUsages } |
    Where-Object { $_.Value -eq $codeSigningOid }
if ($null -eq $allowsCodeSigning) {
    throw 'The selected certificate is not valid for code signing.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($inputPath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $manifestEntry) { throw 'Package manifest is missing.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally {
    $archive.Dispose()
}
$identity = $manifest.Package.Identity
if ($identity.Name -ne 'Long.LongBetterWindows') {
    throw 'Package identity is not Long.LongBetterWindows.'
}
if (-not [string]::Equals(
    $certificate.Subject,
    [string]$identity.Publisher,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Certificate Subject does not match package Publisher '$($identity.Publisher)'."
}

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdkDirectory = Get-ChildItem -LiteralPath $kitsRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [Version]$_.Name } -Descending |
    Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe')
    } |
    Select-Object -First 1
if ($null -eq $sdkDirectory) {
    throw 'Windows SDK SignTool x64 was not found.'
}
$signTool = Join-Path $sdkDirectory.FullName 'x64\signtool.exe'
$reportPath = "$outputPath.signature.json"
$completed = $false
try {
    Copy-Item -LiteralPath $inputPath -Destination $outputPath
    $arguments = @(
        'sign',
        '/fd', 'SHA256',
        '/sha1', $thumbprint,
        '/s', 'My')
    if ($CertificateStoreLocation -eq 'LocalMachine') {
        $arguments += '/sm'
    }
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    $arguments += $outputPath
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) { throw 'SignTool signing failed.' }

    & $signTool verify /pa /v $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'SignTool verification failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $outputPath
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode verification failed: $($signature.StatusMessage)"
    }

    [ordered]@{
        schema_version = 1
        classification = 'signed_sparse_package'
        generated_at = [DateTimeOffset]::UtcNow.ToString('O')
        input_sha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        output_sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        package_identity = [string]$identity.Name
        package_publisher = [string]$identity.Publisher
        certificate_subject = $certificate.Subject
        certificate_thumbprint = $certificate.Thumbprint.ToLowerInvariant()
        certificate_not_after = $certificate.NotAfter.ToUniversalTime().ToString('O')
        timestamp_url = if ([string]::IsNullOrWhiteSpace($TimestampUrl)) { $null } else { $TimestampUrl }
        signature_valid = $true
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $completed = $true
}
finally {
    if (-not $completed) {
        Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Output 'Sparse package signing and verification passed.'
Write-Output "Package: $outputPath"
Write-Output "Report: $reportPath"
