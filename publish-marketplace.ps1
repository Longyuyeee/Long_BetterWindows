#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validate, sign and publish a Long marketplace Registry bundle.
.DESCRIPTION
  The RSA private key is read only by the .NET 8 publisher process and is never copied to output.
.EXAMPLE
  .\publish-marketplace.ps1 -SourceCatalog marketplace-source.json -PackagesDir dist `
    -OutputDir artifacts\marketplace -PrivateKeyPath C:\secure\publisher-private.pem `
    -PublisherKeyId long-2026-01 -PublisherName "Long Labs" `
    -BasePackageUri https://packages.example.com/
#>
param(
    [Parameter(Mandatory=$true)] [string] $SourceCatalog,
    [Parameter(Mandatory=$true)] [string] $PackagesDir,
    [Parameter(Mandatory=$true)] [string] $OutputDir,
    [Parameter(Mandatory=$true)] [string] $PrivateKeyPath,
    [Parameter(Mandatory=$true)] [string] $PublisherKeyId,
    [Parameter(Mandatory=$true)] [string] $PublisherName,
    [Parameter(Mandatory=$true)] [uri] $BasePackageUri,
    [switch] $Force
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
    'run', '--project', $project, '--configuration', 'Release', '--',
    '--source', [IO.Path]::GetFullPath($SourceCatalog),
    '--packages', [IO.Path]::GetFullPath($PackagesDir),
    '--output', [IO.Path]::GetFullPath($OutputDir),
    '--private-key', [IO.Path]::GetFullPath($PrivateKeyPath),
    '--key-id', $PublisherKeyId,
    '--publisher', $PublisherName,
    '--base-uri', $BasePackageUri.AbsoluteUri
)
if ($Force) { $arguments += '--force' }

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Marketplace publisher exited with code $LASTEXITCODE." }
