#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $BundleDir,
    [Parameter(Mandatory=$true)] [string] $PreparationEvidenceDir,
    [Parameter(Mandatory=$true)] [ValidateSet('Local','Https')] [string] $Target,
    [Parameter(Mandatory=$true)] [string] $Destination,
    [Parameter(Mandatory=$true)] [string] $ConfirmReleaseId
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'tools\LongBetterWindows.MarketplacePublisher\LongBetterWindows.MarketplacePublisher.csproj'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet run --project $project --configuration Release -- verify-preparation `
    --bundle ([IO.Path]::GetFullPath($BundleDir)) `
    --evidence ([IO.Path]::GetFullPath($PreparationEvidenceDir)) `
    --target $Target `
    --destination $Destination `
    --confirm-release $ConfirmReleaseId
if ($LASTEXITCODE -ne 0) {
    throw "Marketplace preparation verifier exited with code $LASTEXITCODE."
}
