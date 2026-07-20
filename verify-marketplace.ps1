#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verify a deployed Long marketplace from the public client path.
.EXAMPLE
  .\verify-marketplace.ps1 -RegistryUri https://market.example.com/registry.json `
    -TrustStorePath C:\secure\trusted-publishers.json `
    -AllowedPackageHosts packages.example.com `
    -ReportPath artifacts\marketplace-verification.json
#>
param(
    [Parameter(Mandatory=$true)] [uri] $RegistryUri,
    [Parameter(Mandatory=$true)] [string] $TrustStorePath,
    [string[]] $AllowedPackageHosts = @(),
    [ValidateRange(2,300)] [int] $TimeoutSeconds = 60,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'tools\LongBetterWindows.MarketplacePublisher\LongBetterWindows.MarketplacePublisher.csproj'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw 'dotnet CLI was not found.' }
    $dotnet = $command.Source
}

$arguments = @(
    'run', '--project', $project, '--configuration', 'Release', '--', 'verify',
    '--registry', $RegistryUri.AbsoluteUri,
    '--trust', [IO.Path]::GetFullPath($TrustStorePath),
    '--timeout-seconds', $TimeoutSeconds.ToString()
)
if ($AllowedPackageHosts.Count -gt 0) {
    $arguments += @('--allowed-hosts', ($AllowedPackageHosts -join ','))
}
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $arguments += @('--report', [IO.Path]::GetFullPath($ReportPath))
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Marketplace verifier exited with code $LASTEXITCODE." }
