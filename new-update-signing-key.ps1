#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Creates the local RSA key pair used to sign application update manifests.
.DESCRIPTION
  The private key is written under local-secrets/, which is ignored by Git.
  Only the public key is copied into the application and may be committed.
#>
param(
    [string] $PrivateKeyPath,
    [string] $PublicKeyPath,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PrivateKeyPath)) {
    $PrivateKeyPath = Join-Path $repoRoot 'local-secrets\update-signing\update-signing.private.key'
}
if ([string]::IsNullOrWhiteSpace($PublicKeyPath)) {
    $PublicKeyPath = Join-Path $repoRoot 'src\LongBetterWindows.Host\Update\update-public-key.xml'
}

if ((Test-Path -LiteralPath $PrivateKeyPath) -and -not $Force) {
    throw "Private key already exists: $PrivateKeyPath. Use -Force only for an intentional key rotation."
}

$privateDirectory = Split-Path -Parent $PrivateKeyPath
$publicDirectory = Split-Path -Parent $PublicKeyPath
New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publicDirectory -Force | Out-Null

$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
try {
    [IO.File]::WriteAllText(
        $PrivateKeyPath,
        $rsa.ToXmlString($true),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $PublicKeyPath,
        $rsa.ToXmlString($false),
        [Text.UTF8Encoding]::new($false))
}
finally {
    $rsa.Dispose()
}

# Keep the private key readable only by the account that created it.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl = Get-Acl -LiteralPath $PrivateKeyPath
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    $identity,
    [Security.AccessControl.FileSystemRights]::FullControl,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $PrivateKeyPath -AclObject $acl

Write-Host "Private update signing key created in ignored local-secrets directory."
Write-Host "Public verification key written to src/LongBetterWindows.Host/Update."
