#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Create, independently verify, and dry-run a signed marketplace release bundle.
.DESCRIPTION
  This command never deploys. It requires an RSA package-signing key, not a Windows
  Authenticode certificate. Bundle and evidence directories must not already exist.
#>
param(
    [Parameter(Mandatory=$true)] [string] $SourceCatalog,
    [Parameter(Mandatory=$true)] [string] $PackagesDir,
    [Parameter(Mandatory=$true)] [string] $BundleDir,
    [Parameter(Mandatory=$true)] [string] $EvidenceDir,
    [Parameter(Mandatory=$true)] [string] $PrivateKeyPath,
    [Parameter(Mandatory=$true)] [string] $PublisherKeyId,
    [Parameter(Mandatory=$true)] [string] $PublisherName,
    [Parameter(Mandatory=$true)] [uri] $BasePackageUri,
    [Parameter(Mandatory=$true)] [ValidateSet('Local','Https')] [string] $Target,
    [Parameter(Mandatory=$true)] [string] $Destination
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'tools\LongBetterWindows.MarketplacePublisher\LongBetterWindows.MarketplacePublisher.csproj'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet run --project $project --configuration Release -- prepare `
    --source ([IO.Path]::GetFullPath($SourceCatalog)) `
    --packages ([IO.Path]::GetFullPath($PackagesDir)) `
    --bundle ([IO.Path]::GetFullPath($BundleDir)) `
    --evidence ([IO.Path]::GetFullPath($EvidenceDir)) `
    --private-key ([IO.Path]::GetFullPath($PrivateKeyPath)) `
    --key-id $PublisherKeyId `
    --publisher $PublisherName `
    --base-uri $BasePackageUri.AbsoluteUri `
    --target $Target `
    --destination $Destination
if ($LASTEXITCODE -ne 0) {
    throw "Marketplace release preparation exited with code $LASTEXITCODE."
}
