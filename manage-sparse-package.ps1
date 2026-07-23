#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Status', 'Register', 'Unregister')]
    [string] $Action,
    [string] $PackagePath,
    [string] $ExternalLocation
)

$ErrorActionPreference = 'Stop'
$identityName = 'Long.LongBetterWindows'
$receiptDirectory = Join-Path $env:LOCALAPPDATA 'LongBetterWindows\Integration'
$receiptPath = Join-Path $receiptDirectory 'sparse-package.json'

function Get-InstalledPackage {
    Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending |
        Select-Object -First 1
}

function Read-Receipt {
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { return $null }
    try {
        Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Write-State([bool]$succeeded, [string]$message) {
    $package = Get-InstalledPackage
    $receipt = Read-Receipt
    [ordered]@{
        succeeded = $succeeded
        message = $message
        installed = $null -ne $package
        identity_name = $identityName
        package_full_name = if ($null -eq $package) { $null } else { $package.PackageFullName }
        version = if ($null -eq $package) { $null } else { $package.Version.ToString() }
        publisher = if ($null -eq $package) { $null } else { $package.Publisher }
        architecture = if ($null -eq $package) { $null } else { $package.Architecture.ToString() }
        status = if ($null -eq $package) { 'NotInstalled' } else { $package.Status.ToString() }
        external_location = if (
            $null -ne $package -and
            $null -ne $receipt -and
            $receipt.package_full_name -eq $package.PackageFullName
        ) { $receipt.external_location } else { $null }
        package_sha256 = if (
            $null -ne $package -and
            $null -ne $receipt -and
            $receipt.package_full_name -eq $package.PackageFullName
        ) { $receipt.package_sha256 } else { $null }
    } | ConvertTo-Json -Compress
}

try {
    if ($Action -eq 'Status') {
        Write-State $true 'Status refreshed'
        exit 0
    }

    if ($Action -eq 'Register') {
        $packageFullPath = [IO.Path]::GetFullPath($PackagePath)
        $externalFullPath = [IO.Path]::GetFullPath($ExternalLocation)
        if (-not (Test-Path -LiteralPath $packageFullPath -PathType Leaf)) {
            throw "Candidate package was not found: $packageFullPath"
        }
        if ([IO.Path]::GetExtension($packageFullPath) -notin @('.msix', '.appx')) {
            throw 'Candidate package must use the .msix or .appx extension.'
        }
        $hostPath = Join-Path $externalFullPath 'LongBetterWindows.Host.exe'
        $shellPath = Join-Path $externalFullPath `
            'ShellExtension\LongBetterWindows.ShellExtension.dll'
        if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $shellPath -PathType Leaf)) {
            throw 'External location is missing the host EXE or x64 shell extension DLL.'
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($packageFullPath)
        try {
            $manifestEntry = $archive.GetEntry('AppxManifest.xml')
            if ($null -eq $manifestEntry) { throw 'Candidate package is missing AppxManifest.xml.' }
            $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        finally {
            $archive.Dispose()
        }
        $identity = $manifest.Package.Identity
        if ($identity.Name -ne $identityName -or
            $identity.ProcessorArchitecture -ne 'x64') {
            throw 'Candidate package identity or architecture does not match Long x64.'
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $packageFullPath
        if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
            $null -eq $signature.SignerCertificate) {
            throw 'Candidate package does not have a verifiable valid signature.'
        }
        if (-not [string]::Equals(
            $signature.SignerCertificate.Subject,
            [string]$identity.Publisher,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Signing certificate Subject does not match package Publisher.'
        }

        $candidateVersion = [Version][string]$identity.Version
        $candidateHash = (Get-FileHash `
            -LiteralPath $packageFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $existing = Get-InstalledPackage
        if ($null -ne $existing) {
            if ([Version]$existing.Version -gt $candidateVersion) {
                throw 'Candidate version is older than the registered sparse package.'
            }
            if ([Version]$existing.Version -eq $candidateVersion) {
                $existingReceipt = Read-Receipt
                if ($null -ne $existingReceipt -and
                    $existingReceipt.package_full_name -eq $existing.PackageFullName -and
                    $existingReceipt.external_location -eq $externalFullPath -and
                    $existingReceipt.package_sha256 -eq $candidateHash) {
                    Write-State $true 'The same signed sparse package is already registered.'
                    exit 0
                }
                throw 'The same package version is already registered from different evidence.'
            }
        }

        Add-AppxPackage -Path $packageFullPath -ExternalLocation $externalFullPath `
            -ErrorAction Stop
        $installed = Get-InstalledPackage
        if ($null -eq $installed -or
            $installed.Version.ToString() -ne [string]$identity.Version) {
            throw 'Package status or version validation failed after deployment.'
        }

        [IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
        $temporaryReceipt = "$receiptPath.$PID.tmp"
        [ordered]@{
            schema_version = 1
            package_full_name = $installed.PackageFullName
            package_sha256 = $candidateHash
            external_location = $externalFullPath
            registered_at = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json | Set-Content -LiteralPath $temporaryReceipt -Encoding UTF8
        Move-Item -LiteralPath $temporaryReceipt -Destination $receiptPath -Force
        Write-State $true 'Win11 primary context menu was registered or upgraded.'
        exit 0
    }

    $installed = Get-InstalledPackage
    if ($null -ne $installed) {
        Remove-AppxPackage -Package $installed.PackageFullName -ErrorAction Stop
    }
    if ($null -ne (Get-InstalledPackage)) {
        throw 'Long sparse package remains installed after removal.'
    }
    Remove-Item -LiteralPath $receiptPath -Force -ErrorAction SilentlyContinue
    Write-State $true 'Win11 primary context menu was removed.'
    exit 0
}
catch {
    Write-State $false $_.Exception.Message
    exit 1
}
