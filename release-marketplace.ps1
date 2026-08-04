#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Deploy an approved marketplace preparation and verify it through the public path.
.DESCRIPTION
  Requires an existing public baseline so a failed deployment can be rolled back.
#>
param(
    [Parameter(Mandatory=$true)] [string] $BundleDir,
    [Parameter(Mandatory=$true)] [string] $PreparationEvidenceDir,
    [Parameter(Mandatory=$true)] [uri] $Destination,
    [Parameter(Mandatory=$true)] [string] $TrustStorePath,
    [string[]] $AllowedPackageHosts = @(),
    [string] $CredentialEnvironmentVariable = 'LONG_MARKETPLACE_DEPLOY_TOKEN',
    [Parameter(Mandatory=$true)] [string] $ExecutionEvidenceDir,
    [Parameter(Mandatory=$true)] [string] $ConfirmReleaseId
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
if ($Destination.Scheme -ne 'https' -or -not $Destination.AbsolutePath.EndsWith('/')) {
    throw 'Marketplace release destination must use HTTPS and end with a slash.'
}
if ([string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable($CredentialEnvironmentVariable))) {
    throw "Marketplace release credential environment variable is missing: $CredentialEnvironmentVariable"
}

$bundleRoot = [IO.Path]::GetFullPath($BundleDir)
$preparationRoot = [IO.Path]::GetFullPath($PreparationEvidenceDir)
$evidenceRoot = [IO.Path]::GetFullPath($ExecutionEvidenceDir)
$trustStore = [IO.Path]::GetFullPath($TrustStorePath)
if (-not (Test-Path -LiteralPath $trustStore -PathType Leaf)) {
    throw "Marketplace trust store was not found: $trustStore"
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Execution evidence directory already exists: $evidenceRoot"
}

$verifyPreparation = Join-Path $PSScriptRoot 'verify-marketplace-preparation.ps1'
$deploy = Join-Path $PSScriptRoot 'deploy-marketplace.ps1'
$verify = Join-Path $PSScriptRoot 'verify-marketplace.ps1'
$rollback = Join-Path $PSScriptRoot 'rollback-marketplace.ps1'
& $verifyPreparation -BundleDir $bundleRoot `
    -PreparationEvidenceDir $preparationRoot `
    -Target Https -Destination $Destination.AbsoluteUri `
    -ConfirmReleaseId $ConfirmReleaseId

[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
$baselinePath = Join-Path $evidenceRoot 'baseline-verification.json'
$deploymentPath = Join-Path $evidenceRoot 'deployment.json'
$deployedPath = Join-Path $evidenceRoot 'deployed-verification.json'
$rollbackPath = Join-Path $evidenceRoot 'rollback-verification.json'
$summaryPath = Join-Path $evidenceRoot 'release-summary.json'
$registryUri = [uri]::new($Destination, 'registry.json')
$summary = [ordered]@{
    schema_version = 1
    started_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'marketplace_https_release'
    passed = $false
    release_id = $ConfirmReleaseId
    destination = $Destination.AbsoluteUri
    preparation_summary_sha256 = (Get-FileHash -LiteralPath `
        (Join-Path $preparationRoot 'preparation-summary.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    preparation_verified = $true
    baseline_verified = $false
    deployment_started = $false
    deployment_completed = $false
    deployment_verified = $false
    deployed_matches_preparation = $false
    rollback_completed = $false
    rollback_verified = $false
    baseline_restored = $false
    failure = $null
    rollback_failure = $null
    rollback_verification_failure = $null
    completed_at = $null
    evidence = $null
}

function Get-VerificationIdentity {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $report = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $packages = @($report.Packages | Sort-Object PluginId, Version | ForEach-Object {
        "$($_.PluginId)|$($_.Version)|$($_.Sha256)|$($_.PublisherKeyId)|$($_.Bytes)"
    })
    return "$($report.RegistryGeneratedAt)|$($report.EntryCount)|$($report.PackageCount)|$($packages -join ';')"
}

function Get-PackageIdentity {
    param([Parameter(Mandatory=$true)] [string] $Path)
    $report = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    return (@($report.Packages | Sort-Object PluginId, Version | ForEach-Object {
        "$($_.PluginId)|$($_.Version)|$($_.Sha256)|$($_.PublisherKeyId)|$($_.Bytes)"
    }) -join ';')
}

try {
    & $verify -RegistryUri $registryUri -TrustStorePath $trustStore `
        -AllowedPackageHosts $AllowedPackageHosts -ReportPath $baselinePath
    $baselineIdentity = Get-VerificationIdentity -Path $baselinePath
    $summary.baseline_verified = $true
    $preparedIdentity = Get-PackageIdentity -Path `
        (Join-Path $preparationRoot 'bundle-verification.json')

    & $verifyPreparation -BundleDir $bundleRoot `
        -PreparationEvidenceDir $preparationRoot `
        -Target Https -Destination $Destination.AbsoluteUri `
        -ConfirmReleaseId $ConfirmReleaseId
    $summary.deployment_started = $true
    & $deploy -BundleDir $bundleRoot -Target Https `
        -Destination $Destination.AbsoluteUri `
        -CredentialEnvironmentVariable $CredentialEnvironmentVariable `
        -ResultPath $deploymentPath
    $deploymentReport = Get-Content -LiteralPath $deploymentPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($deploymentReport.Mode -ne 'deployed' `
        -or [string]$deploymentReport.ReleaseId -ne $ConfirmReleaseId) {
        throw 'Deployment report differs from the approved Release ID or mode.'
    }
    $summary.deployment_completed = $true

    & $verify -RegistryUri $registryUri -TrustStorePath $trustStore `
        -AllowedPackageHosts $AllowedPackageHosts -ReportPath $deployedPath
    if ((Get-PackageIdentity -Path $deployedPath) -ne $preparedIdentity) {
        throw 'Public marketplace package set differs from the approved preparation.'
    }
    $summary.deployment_verified = $true
    $summary.deployed_matches_preparation = $true
    $summary.passed = $true
    $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
}
catch {
    $summary.failure = $_.Exception.Message
    throw
}
finally {
    if ($summary.deployment_started -and -not $summary.deployment_verified) {
        try {
            & $rollback -Target Https -Destination $Destination.AbsoluteUri `
                -ReleaseId $ConfirmReleaseId -ConfirmReleaseId $ConfirmReleaseId `
                -CredentialEnvironmentVariable $CredentialEnvironmentVariable
            $summary.rollback_completed = $true
        }
        catch {
            $summary.rollback_failure = $_.Exception.Message
        }
    }
    if ($summary.rollback_completed) {
        try {
            & $verify -RegistryUri $registryUri -TrustStorePath $trustStore `
                -AllowedPackageHosts $AllowedPackageHosts -ReportPath $rollbackPath
            $summary.rollback_verified = $true
            $summary.baseline_restored = (Get-VerificationIdentity -Path $rollbackPath) `
                -eq $baselineIdentity
            if (-not $summary.baseline_restored) {
                throw 'Rollback public state differs from the verified baseline.'
            }
        }
        catch {
            $summary.rollback_verification_failure = $_.Exception.Message
        }
    }
    if ([string]::IsNullOrWhiteSpace([string]$summary.completed_at)) {
        $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $evidence = [ordered]@{}
    foreach ($path in @($baselinePath, $deploymentPath, $deployedPath, $rollbackPath)) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $evidence[[IO.Path]::GetFileNameWithoutExtension($path)] = [ordered]@{
                file = [IO.Path]::GetFileName($path)
                sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }
    $summary.evidence = $evidence
    Write-NewJsonFileAtomically -Value $summary -Path $summaryPath `
        -Depth 6 -Label 'Marketplace release summary'
}

Write-Output "Marketplace release completed: $ConfirmReleaseId"
Write-Output "Evidence: $evidenceRoot"
