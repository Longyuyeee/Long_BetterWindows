#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Build a per-user Setup.exe from the Long Assistant self-contained publish directory.
.DESCRIPTION
  The installer defaults to %LOCALAPPDATA%\Programs\LongAssistant and needs no elevation.
  The input is validated for the host executable, plugin count, and command count.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $Version = '1.11.0-rc.12',

    [string] $NumericVersion = '1.11.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releasePolicyScript = Join-Path $repoRoot 'release-policy.ps1'
if (-not (Test-Path -LiteralPath $releasePolicyScript -PathType Leaf)) {
    throw "Release policy was not found: $releasePolicyScript"
}
. $releasePolicyScript
$releasePolicy = New-LongUnsignedReleasePolicy -Version $Version
$installerScript = Join-Path $repoRoot 'installer\LongAssistant.iss'
$source = [IO.Path]::GetFullPath($SourceDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$expectedPluginCount = 25
$expectedCommandCount = 42

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Self-contained publish directory does not exist: $source"
}

$hostExecutable = Join-Path $source 'LongBetterWindows.Host.exe'
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "Host executable is missing from the publish directory: $hostExecutable"
}

$manifestFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $source 'Plugins') `
        -Recurse -File -Filter manifest.json
)
if ($manifestFiles.Count -ne $expectedPluginCount) {
    throw "Installer input must contain $expectedPluginCount plugin manifests; found $($manifestFiles.Count)."
}

$pluginManifests = @(
    $manifestFiles | ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
)
$uniquePluginIds = @($pluginManifests.id | Sort-Object -Unique)
$commandCount = (
    $pluginManifests | ForEach-Object { @($_.commands).Count } |
        Measure-Object -Sum
).Sum
if ($uniquePluginIds.Count -ne $expectedPluginCount -or
    $commandCount -ne $expectedCommandCount) {
    throw "Installer input contract mismatch: UniquePlugins=$($uniquePluginIds.Count), Commands=$commandCount"
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$iscc = $isccCandidates | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
} | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { $iscc = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup'
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

$compilerOutput = @(& $iscc `
    "/DSourceDir=$source" `
    "/DAppVersion=$Version" `
    "/DNumericVersion=$NumericVersion" `
    "/O$output" `
    $installerScript 2>&1)
if ($LASTEXITCODE -ne 0) {
    $tail = $compilerOutput | Select-Object -Last 20
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE`n$($tail -join [Environment]::NewLine)"
}

$installerName = "LongAssistant-Setup-v$Version.exe"
$installerPath = Join-Path $output $installerName
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Inno Setup completed but the expected installer is missing: $installerPath"
}

$installerInfo = Get-Item -LiteralPath $installerPath
if ($installerInfo.Length -lt 1MB) {
    throw "Installer size is unexpectedly small: $($installerInfo.Length) bytes"
}

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($installerPath)
if ($versionInfo.ProductVersion.Trim() -ne $NumericVersion) {
    throw "Installer version resource mismatch: Product=$($versionInfo.ProductName), Version=$($versionInfo.ProductVersion)"
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    file = $installerName
    kind = 'installer'
    format = 'inno-setup-exe'
    install_scope = 'current-user'
    installer_privileges = $releasePolicy.installer_privileges
    requires_elevation = $false
    distribution_channel = $releasePolicy.distribution_channel
    publisher_identity = $releasePolicy.publisher_identity
    authenticode_status = $releasePolicy.authenticode_status
    sha256 = $hash
    bytes = $installerInfo.Length
    plugins = $uniquePluginIds.Count
    commands = $commandCount
    signed = $releasePolicy.signed
} | ConvertTo-Json -Depth 3
