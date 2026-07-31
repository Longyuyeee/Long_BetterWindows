#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $BundleDirectory,
    [Parameter(Mandatory=$true)] [string] $ExpectedPublisherKeyId,
    [Parameter(Mandatory=$true)] [ValidatePattern('^[0-9A-Fa-f]{64}$')] [string] $ExpectedPublicKeyFingerprint,
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'tools\LongBetterWindows.MarketplacePublisher\LongBetterWindows.MarketplacePublisher.csproj'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$arguments = @(
    'run', '--project', $project, '--configuration', 'Release', '--', 'verify-bundle',
    '--bundle', [IO.Path]::GetFullPath($BundleDirectory),
    '--key-id', $ExpectedPublisherKeyId,
    '--fingerprint', $ExpectedPublicKeyFingerprint
)
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $arguments += @('--report', [IO.Path]::GetFullPath($ReportPath))
}
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Marketplace bundle verifier exited with code $LASTEXITCODE." }
