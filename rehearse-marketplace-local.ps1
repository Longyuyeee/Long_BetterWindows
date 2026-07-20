#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Run a local marketplace publish, deploy, upgrade and rollback rehearsal.
.DESCRIPTION
  Creates an ephemeral RSA key and two signed Base64 plugin versions. The private
  key and build workspace are always removed; only machine-readable evidence and
  the rolled-back local deployment are retained.
.EXAMPLE
  .\rehearse-marketplace-local.ps1 `
    -OutputDirectory .\artifacts\quality\marketplace-local-release-rehearsal
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Local marketplace rehearsal output already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$workRoot = Join-Path $outputRoot '.private-work'
$deploymentRoot = Join-Path $outputRoot 'deployed-marketplace'
$evidenceRoot = Join-Path $outputRoot 'evidence'
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
$privateKeyPath = Join-Path $workRoot 'rehearsal.private.pem'
$summaryPath = Join-Path $evidenceRoot 'local-rehearsal-summary.json'
$rsa = $null
$summary = [ordered]@{
    schema_version = 1
    started_at = [DateTimeOffset]::UtcNow.ToString('O')
    completed_at = $null
    plugin_id = 'com.long.base64'
    baseline_version = $null
    candidate_version = $null
    baseline_release_id = $null
    candidate_release_id = $null
    dry_run_verified = $false
    candidate_deployed = $false
    rollback_completed = $false
    rollback_registry_hash_matches = $false
    rollback_packages_available = $false
    ephemeral_private_key_deleted = $false
    temporary_deployment_directories = @()
    failure = $null
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public static class LongLocalRehearsalPem {
    public static string ExportPrivateKey(RSA rsa) {
        var p = rsa.ExportParameters(true);
        var body = new List<byte>();
        body.AddRange(Integer(new byte[] { 0 }));
        body.AddRange(Integer(p.Modulus));
        body.AddRange(Integer(p.Exponent));
        body.AddRange(Integer(p.D));
        body.AddRange(Integer(p.P));
        body.AddRange(Integer(p.Q));
        body.AddRange(Integer(p.DP));
        body.AddRange(Integer(p.DQ));
        body.AddRange(Integer(p.InverseQ));
        var der = Value(0x30, body.ToArray());
        var text = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks)
            .Replace("\r\n", "\n");
        return "-----BEGIN RSA PRIVATE KEY-----\n" + text +
            "\n-----END RSA PRIVATE KEY-----";
    }
    private static byte[] Integer(byte[] value) {
        var start = 0;
        while (start < value.Length - 1 && value[start] == 0) start++;
        var content = new List<byte>();
        if ((value[start] & 0x80) != 0) content.Add(0);
        for (var i = start; i < value.Length; i++) content.Add(value[i]);
        return Value(0x02, content.ToArray());
    }
    private static byte[] Value(byte tag, byte[] content) {
        var result = new List<byte> { tag };
        result.AddRange(Length(content.Length));
        result.AddRange(content);
        return result.ToArray();
    }
    private static byte[] Length(int length) {
        if (length < 128) return new byte[] { (byte)length };
        var bytes = new List<byte>();
        for (var value = length; value > 0; value >>= 8)
            bytes.Insert(0, (byte)(value & 0xff));
        bytes.Insert(0, (byte)(0x80 | bytes.Count));
        return bytes.ToArray();
    }
}
'@

function Write-JsonUtf8([string] $Path, $Value) {
    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function New-SignedBundle([string] $Version, [string] $Name) {
    $bundleWork = Join-Path $workRoot $Name
    $pluginRoot = Join-Path $bundleWork 'plugin'
    $packagesRoot = Join-Path $bundleWork 'packages-source'
    $bundleRoot = Join-Path $bundleWork 'bundle'
    [IO.Directory]::CreateDirectory($pluginRoot) | Out-Null
    [IO.Directory]::CreateDirectory($packagesRoot) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\Base64Tool\index.html') -Destination $pluginRoot
    $manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Base64Tool\manifest.json') `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest.version = $Version
    Write-JsonUtf8 (Join-Path $pluginRoot 'manifest.json') $manifest

    $packageName = "com-long-base64-v$Version.lpak"
    $zipPath = Join-Path $packagesRoot "$packageName.zip"
    Compress-Archive -Path (Join-Path $pluginRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Move-Item -LiteralPath $zipPath -Destination (Join-Path $packagesRoot $packageName)

    $source = [ordered]@{
        SchemaVersion = 1
        Entries = @([ordered]@{
            Id = 'com.long.base64'
            Name = 'Base64 Tool'
            Summary = 'Local release-gate rehearsal package'
            Description = 'Validates signing, deployment, replacement and rollback only.'
            Category = 'Developer Tools'
            Tags = @('rehearsal', 'base64')
            Versions = @([ordered]@{
                Version = $Version
                PackageFile = $packageName
                PublishedAt = [DateTimeOffset]::UtcNow.ToString('O')
                ReleaseNotes = "$Name rehearsal release"
            })
        })
    }
    $sourcePath = Join-Path $bundleWork 'marketplace-source.json'
    Write-JsonUtf8 $sourcePath $source
    $publishOutput = & (Join-Path $repoRoot 'publish-marketplace.ps1') `
        -SourceCatalog $sourcePath -PackagesDir $packagesRoot -OutputDir $bundleRoot `
        -PrivateKeyPath $privateKeyPath -PublisherKeyId 'local-rehearsal-2026' `
        -PublisherName 'Long Local Rehearsal' `
        -BasePackageUri ([uri]'https://local-rehearsal.invalid/packages/')
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish $Name bundle." }
    $publishOutput | ForEach-Object { Write-Host $_ }
    return $bundleRoot
}

function Get-RegistryVersion([string] $RegistryPath) {
    $registry = Get-Content -LiteralPath $RegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return [string]$registry.Entries[0].Versions[0].Version
}

function Test-RegistryPackages([string] $RegistryPath, [string] $DeploymentPath) {
    $registry = Get-Content -LiteralPath $RegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in @($registry.Entries)) {
        foreach ($version in @($entry.Versions)) {
            $name = [IO.Path]::GetFileName(([uri]$version.PackageUri).AbsolutePath)
            if (-not (Test-Path -LiteralPath (Join-Path $DeploymentPath "packages\$name"))) {
                return $false
            }
        }
    }
    return $true
}

try {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    [IO.File]::WriteAllText(
        $privateKeyPath, [LongLocalRehearsalPem]::ExportPrivateKey($rsa),
        [Text.UTF8Encoding]::new($false))

    $sourceManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Base64Tool\manifest.json') `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $baselineVersion = [Version]$sourceManifest.version
    $candidateVersion = [Version]::new(
        $baselineVersion.Major, $baselineVersion.Minor, $baselineVersion.Build + 1)
    $summary.baseline_version = $baselineVersion.ToString(3)
    $summary.candidate_version = $candidateVersion.ToString(3)

    $baselineBundle = New-SignedBundle $summary.baseline_version 'baseline'
    Start-Sleep -Milliseconds 1100
    $candidateBundle = New-SignedBundle $summary.candidate_version 'candidate'

    $baselineReport = Join-Path $evidenceRoot 'baseline-deployment.json'
    & (Join-Path $repoRoot 'deploy-marketplace.ps1') -BundleDir $baselineBundle `
        -Target Local -Destination $deploymentRoot -ResultPath $baselineReport
    $baselineDeployment = Get-Content $baselineReport -Raw -Encoding UTF8 | ConvertFrom-Json
    $summary.baseline_release_id = [string]$baselineDeployment.ReleaseId
    $baselineRegistry = Join-Path $deploymentRoot 'registry.json'
    if ((Get-RegistryVersion $baselineRegistry) -ne $summary.baseline_version) {
        throw 'Baseline deployment Registry version mismatch.'
    }
    $baselineHash = (Get-FileHash $baselineRegistry -Algorithm SHA256).Hash

    $dryRunReport = Join-Path $evidenceRoot 'candidate-dry-run.json'
    & (Join-Path $repoRoot 'deploy-marketplace.ps1') -BundleDir $candidateBundle `
        -Target Local -Destination $deploymentRoot -ResultPath $dryRunReport -DryRun
    $dryRun = Get-Content $dryRunReport -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($dryRun.Mode -ne 'dry_run' -or (Get-FileHash $baselineRegistry -Algorithm SHA256).Hash -ne $baselineHash) {
        throw 'Candidate dry run changed the deployed Registry.'
    }
    $summary.dry_run_verified = $true

    $candidateReport = Join-Path $evidenceRoot 'candidate-deployment.json'
    & (Join-Path $repoRoot 'deploy-marketplace.ps1') -BundleDir $candidateBundle `
        -Target Local -Destination $deploymentRoot -ResultPath $candidateReport -Force
    $candidateDeployment = Get-Content $candidateReport -Raw -Encoding UTF8 | ConvertFrom-Json
    $summary.candidate_release_id = [string]$candidateDeployment.ReleaseId
    if ((Get-RegistryVersion $baselineRegistry) -ne $summary.candidate_version) {
        throw 'Candidate deployment Registry version mismatch.'
    }
    $candidateHash = (Get-FileHash $baselineRegistry -Algorithm SHA256).Hash
    if ($candidateHash -eq $baselineHash) { throw 'Candidate Registry did not change.' }
    $summary.candidate_deployed = $true

    & (Join-Path $repoRoot 'rollback-marketplace.ps1') -Target Local `
        -Destination $deploymentRoot -ReleaseId $summary.candidate_release_id `
        -ConfirmReleaseId $summary.candidate_release_id
    $summary.rollback_completed = $true
    $restoredHash = (Get-FileHash $baselineRegistry -Algorithm SHA256).Hash
    $summary.rollback_registry_hash_matches = $restoredHash -eq $baselineHash
    $summary.rollback_packages_available = Test-RegistryPackages $baselineRegistry $deploymentRoot
    if (-not $summary.rollback_registry_hash_matches -or -not $summary.rollback_packages_available `
        -or (Get-RegistryVersion $baselineRegistry) -ne $summary.baseline_version) {
        throw 'Rolled-back local marketplace is not internally consistent.'
    }
    $summary.completed_at = [DateTimeOffset]::UtcNow.ToString('O')
}
catch {
    $summary.failure = $_.Exception.Message
    throw
}
finally {
    if ($null -ne $rsa) { $rsa.Dispose() }
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
    $summary.ephemeral_private_key_deleted = -not (Test-Path -LiteralPath $privateKeyPath)
    $summary.temporary_deployment_directories = @(
        Get-ChildItem -LiteralPath $outputRoot -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '.market-deploy-*' } |
            ForEach-Object FullName)
    Write-JsonUtf8 $summaryPath $summary
}

Write-Output "Local marketplace release rehearsal passed: $outputRoot"
