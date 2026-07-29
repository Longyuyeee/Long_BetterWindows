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
    [switch] $PreflightOnly,
    [switch] $ConfirmRehearsal
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
if (-not $PreflightOnly -and -not $ConfirmRehearsal) {
    throw 'Marketplace rehearsal requires -ConfirmRehearsal because it deploys and then rolls back a live Registry.'
}
if ($Destination.Scheme -ne 'https') { throw 'Marketplace rehearsal destination must use HTTPS.' }
if (-not $Destination.AbsolutePath.EndsWith('/')) {
    throw 'Marketplace rehearsal destination must end with a slash.'
}
if (-not $PreflightOnly -and [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable($CredentialEnvironmentVariable))) {
    throw "Marketplace rehearsal credential environment variable is missing: $CredentialEnvironmentVariable"
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$deployScript = Join-Path $repoRoot 'deploy-marketplace.ps1'
$verifyScript = Join-Path $repoRoot 'verify-marketplace.ps1'
$rollbackScript = Join-Path $repoRoot 'rollback-marketplace.ps1'
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
$bundleRoot = [IO.Path]::GetFullPath($BundleDir)
$trustStore = [IO.Path]::GetFullPath($TrustStorePath)
if (-not (Test-Path -LiteralPath $bundleRoot -PathType Container)) {
    throw "Marketplace rehearsal bundle directory was not found: $bundleRoot"
}
if (-not (Test-Path -LiteralPath $trustStore -PathType Leaf)) {
    throw "Marketplace rehearsal trust store was not found: $trustStore"
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Evidence directory already exists: $evidenceRoot"
}
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null

$deploymentReport = Join-Path $evidenceRoot 'deployment.json'
$dryRunReport = Join-Path $evidenceRoot 'preflight-dry-run.json'
$baselineVerification = Join-Path $evidenceRoot 'baseline-verification.json'
$deployedVerification = Join-Path $evidenceRoot 'deployed-verification.json'
$rollbackVerification = Join-Path $evidenceRoot 'rollback-verification.json'
$summaryPath = Join-Path $evidenceRoot 'rehearsal-summary.json'
$summary = [ordered]@{
    schema_version = 2
    started_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'marketplace_https_rehearsal'
    passed = $false
    destination = $Destination.AbsoluteUri
    preflight_only = [bool]$PreflightOnly
    release_id = $null
    preflight_dry_run_verified = $false
    baseline_verified = $false
    deployment_started = $false
    deployment_completed = $false
    deployment_verified = $false
    rollback_completed = $false
    rollback_verified = $false
    failure = $null
    rollback_failure = $null
    rollback_verification_failure = $null
    completed_at = $null
    evidence = $null
}

try {
    & $deployScript -BundleDir $bundleRoot -Target Https -Destination $Destination.AbsoluteUri `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable `
        -ResultPath $dryRunReport -DryRun
    $dryRun = Get-Content -LiteralPath $dryRunReport -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($dryRun.Mode -ne 'dry_run' -or [string]::IsNullOrWhiteSpace([string]$dryRun.ReleaseId) `
        -or @($dryRun.Files).Count -eq 0 -or $dryRun.Files[-1].Kind -ne 'RegistryCommit') {
        throw 'Marketplace rehearsal Dry Run report is incomplete or does not commit Registry last.'
    }
    $summary.release_id = [string]$dryRun.ReleaseId
    $summary.preflight_dry_run_verified = $true

    $registryUri = [uri]::new($Destination, 'registry.json')
    & $verifyScript -RegistryUri $registryUri -TrustStorePath $trustStore `
        -AllowedPackageHosts $AllowedPackageHosts -ReportPath $baselineVerification
    $summary.baseline_verified = $true

    if ($PreflightOnly) {
        $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
        Write-Output "Marketplace production preflight passed: $($summary.release_id)"
        return
    }

    $summary.deployment_started = $true
    & $deployScript -BundleDir $bundleRoot -Target Https -Destination $Destination.AbsoluteUri `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable -ResultPath $deploymentReport
    $deployment = Get-Content -LiteralPath $deploymentReport -Raw -Encoding UTF8 | ConvertFrom-Json
    $releaseId = [string]$deployment.ReleaseId
    if ([string]::IsNullOrWhiteSpace($releaseId)) { throw 'Deployment report did not contain a release ID.' }
    if ($releaseId -ne [string]$summary.release_id) {
        throw 'Deployment release ID differs from the validated Dry Run plan.'
    }
    $summary.deployment_completed = $true

    & $verifyScript -RegistryUri $registryUri `
        -TrustStorePath $trustStore -AllowedPackageHosts $AllowedPackageHosts `
        -ReportPath $deployedVerification
    $summary.deployment_verified = $true

    & $rollbackScript -Target Https -Destination $Destination.AbsoluteUri `
        -ReleaseId $releaseId -ConfirmReleaseId $releaseId `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable
    $summary.rollback_completed = $true

    & $verifyScript -RegistryUri $registryUri `
        -TrustStorePath $trustStore -AllowedPackageHosts $AllowedPackageHosts `
        -ReportPath $rollbackVerification
    $summary.rollback_verified = $true
    $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
}
catch {
    $summary.failure = $_.Exception.Message
    throw
}
finally {
    if ($summary.deployment_started `
        -and -not [string]::IsNullOrWhiteSpace([string]$summary.release_id) `
        -and -not $summary.rollback_completed) {
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
            & $verifyScript -RegistryUri $registryUri `
                -TrustStorePath $trustStore -AllowedPackageHosts $AllowedPackageHosts `
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
    $summary.passed = -not $summary.preflight_only `
        -and $summary.preflight_dry_run_verified `
        -and $summary.baseline_verified `
        -and $summary.deployment_completed `
        -and $summary.deployment_verified `
        -and $summary.rollback_completed `
        -and $summary.rollback_verified `
        -and [string]::IsNullOrWhiteSpace([string]$summary.failure) `
        -and [string]::IsNullOrWhiteSpace([string]$summary.rollback_failure) `
        -and [string]::IsNullOrWhiteSpace([string]$summary.rollback_verification_failure)
    if ($summary.passed) {
        $evidenceFiles = [ordered]@{
            preflight_dry_run = $dryRunReport
            baseline_verification = $baselineVerification
            deployment = $deploymentReport
            deployed_verification = $deployedVerification
            rollback_verification = $rollbackVerification
        }
        $lockedEvidence = [ordered]@{}
        foreach ($entry in $evidenceFiles.GetEnumerator()) {
            if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
                throw "Marketplace rehearsal evidence file is missing: $($entry.Value)"
            }
            $lockedEvidence[$entry.Key] = [ordered]@{
                file = [IO.Path]::GetFileName($entry.Value)
                sha256 = (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).
                    Hash.ToLowerInvariant()
            }
        }
        $summary.evidence = $lockedEvidence
    }
    Write-NewJsonFileAtomically `
        -Value $summary `
        -Path $summaryPath `
        -Depth 5 `
        -Label 'Marketplace rehearsal summary'
}

Write-Output "Marketplace rehearsal completed and rolled back: $($summary.release_id)"
Write-Output "Evidence: $evidenceRoot"
