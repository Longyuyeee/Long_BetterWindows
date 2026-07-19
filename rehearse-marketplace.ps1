#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Run a controlled deploy, public verification, rollback, and second verification rehearsal.
.DESCRIPTION
  This intentionally returns the Registry to its previous version. It requires an existing rollback point.
#>
param(
    [Parameter(Mandatory=$true)] [string] $BundleDir,
    [Parameter(Mandatory=$true)] [uri] $Destination,
    [Parameter(Mandatory=$true)] [string] $TrustStorePath,
    [string[]] $AllowedPackageHosts = @(),
    [string] $CredentialEnvironmentVariable = 'LONG_MARKETPLACE_DEPLOY_TOKEN',
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [switch] $ConfirmRehearsal
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmRehearsal) {
    throw 'Marketplace rehearsal requires -ConfirmRehearsal because it deploys and then rolls back a live Registry.'
}
if ($Destination.Scheme -ne 'https') { throw 'Marketplace rehearsal destination must use HTTPS.' }
if (-not $Destination.AbsolutePath.EndsWith('/')) {
    throw 'Marketplace rehearsal destination must end with a slash.'
}
$credential = [Environment]::GetEnvironmentVariable($CredentialEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($credential)) {
    throw "Marketplace rehearsal credential environment variable is missing: $CredentialEnvironmentVariable"
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$deployScript = Join-Path $repoRoot 'deploy-marketplace.ps1'
$verifyScript = Join-Path $repoRoot 'verify-marketplace.ps1'
$rollbackScript = Join-Path $repoRoot 'rollback-marketplace.ps1'
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Evidence directory already exists: $evidenceRoot"
}
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

$deploymentReport = Join-Path $evidenceRoot 'deployment.json'
$deployedVerification = Join-Path $evidenceRoot 'deployed-verification.json'
$rollbackVerification = Join-Path $evidenceRoot 'rollback-verification.json'
$summaryPath = Join-Path $evidenceRoot 'rehearsal-summary.json'
$summary = [ordered]@{
    schema_version = 1
    started_at = [DateTimeOffset]::UtcNow.ToString('O')
    destination = $Destination.AbsoluteUri
    release_id = $null
    deployment_verified = $false
    rollback_completed = $false
    rollback_verified = $false
    failure = $null
    rollback_failure = $null
    rollback_verification_failure = $null
    completed_at = $null
}

try {
    & $deployScript -BundleDir $BundleDir -Target Https -Destination $Destination.AbsoluteUri `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable -ResultPath $deploymentReport
    $deployment = Get-Content -LiteralPath $deploymentReport -Raw -Encoding UTF8 | ConvertFrom-Json
    $releaseId = [string]$deployment.ReleaseId
    if ([string]::IsNullOrWhiteSpace($releaseId)) { throw 'Deployment report did not contain a release ID.' }
    $summary.release_id = $releaseId

    & $verifyScript -RegistryUri ([uri]::new($Destination, 'registry.json')) `
        -TrustStorePath $TrustStorePath -AllowedPackageHosts $AllowedPackageHosts `
        -ReportPath $deployedVerification
    $summary.deployment_verified = $true

    & $rollbackScript -Target Https -Destination $Destination.AbsoluteUri `
        -ReleaseId $releaseId -ConfirmReleaseId $releaseId `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable
    $summary.rollback_completed = $true

    & $verifyScript -RegistryUri ([uri]::new($Destination, 'registry.json')) `
        -TrustStorePath $TrustStorePath -AllowedPackageHosts $AllowedPackageHosts `
        -ReportPath $rollbackVerification
    $summary.rollback_verified = $true
    $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
}
catch {
    $summary.failure = $_.Exception.Message
    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace([string]$summary.release_id) -and -not $summary.rollback_completed) {
        try {
            & $rollbackScript -Target Https -Destination $Destination.AbsoluteUri `
                -ReleaseId $summary.release_id -ConfirmReleaseId $summary.release_id `
                -CredentialEnvironmentVariable $CredentialEnvironmentVariable
            $summary.rollback_completed = $true
        }
        catch {
            $summary.rollback_failure = $_.Exception.Message
        }
    }
    if ($summary.rollback_completed -and -not $summary.rollback_verified) {
        try {
            & $verifyScript -RegistryUri ([uri]::new($Destination, 'registry.json')) `
                -TrustStorePath $TrustStorePath -AllowedPackageHosts $AllowedPackageHosts `
                -ReportPath $rollbackVerification
            $summary.rollback_verified = $true
        }
        catch {
            $summary.rollback_verification_failure = $_.Exception.Message
        }
    }
    if ($summary.rollback_verified -and [string]::IsNullOrWhiteSpace([string]$summary.completed_at)) {
        $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
}

Write-Output "Marketplace rehearsal completed and rolled back: $($summary.release_id)"
Write-Output "Evidence: $evidenceRoot"
