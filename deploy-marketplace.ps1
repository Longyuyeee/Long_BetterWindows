#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validate and deploy a generated Long marketplace bundle.
.EXAMPLE
  .\deploy-marketplace.ps1 -BundleDir artifacts\marketplace -Target Local `
    -Destination C:\staging\market -DryRun
.EXAMPLE
  $env:LONG_MARKETPLACE_DEPLOY_TOKEN = '<secret from the deployment vault>'
  .\deploy-marketplace.ps1 -BundleDir artifacts\marketplace -Target Https `
    -Destination https://market.example.com/ -CredentialEnvironmentVariable LONG_MARKETPLACE_DEPLOY_TOKEN
#>
param(
    [Parameter(Mandatory=$true)] [string] $BundleDir,
    [Parameter(Mandatory=$true)] [ValidateSet('Local','Https')] [string] $Target,
    [Parameter(Mandatory=$true)] [string] $Destination,
    [string] $CredentialEnvironmentVariable = 'LONG_MARKETPLACE_DEPLOY_TOKEN',
    [string] $ResultPath,
    [switch] $Force,
    [switch] $DryRun
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
    'run', '--project', $project, '--configuration', 'Release', '--', 'deploy',
    '--bundle', [IO.Path]::GetFullPath($BundleDir),
    '--target', $Target,
    '--destination', $Destination,
    '--credential-env', $CredentialEnvironmentVariable
)
if ($Force) { $arguments += '--force' }
if ($DryRun) { $arguments += '--dry-run' }
if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
    $arguments += @('--result', [IO.Path]::GetFullPath($ResultPath))
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Marketplace deployer exited with code $LASTEXITCODE." }
