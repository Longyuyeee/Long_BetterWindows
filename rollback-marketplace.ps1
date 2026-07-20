#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Restore the Registry snapshot captured before a Long marketplace deployment.
.EXAMPLE
  .\rollback-marketplace.ps1 -Target Local -Destination C:\staging\market `
    -ReleaseId 20260719093000-ABCDEF123456 -ConfirmReleaseId 20260719093000-ABCDEF123456
#>
param(
    [Parameter(Mandatory=$true)] [ValidateSet('Local','Https')] [string] $Target,
    [Parameter(Mandatory=$true)] [string] $Destination,
    [Parameter(Mandatory=$true)] [string] $ReleaseId,
    [Parameter(Mandatory=$true)] [string] $ConfirmReleaseId,
    [string] $CredentialEnvironmentVariable = 'LONG_MARKETPLACE_DEPLOY_TOKEN'
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
    'run', '--project', $project, '--configuration', 'Release', '--', 'rollback',
    '--target', $Target,
    '--destination', $Destination,
    '--release', $ReleaseId,
    '--confirm-release', $ConfirmReleaseId,
    '--credential-env', $CredentialEnvironmentVariable
)
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Marketplace rollback exited with code $LASTEXITCODE." }
