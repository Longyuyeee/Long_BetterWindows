#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $EvidenceDirectory,
    [Parameter(Mandatory=$true)] [string] $WpaProfilePath,
    [string] $OutputDirectory,
    [string] $ExpectedCommit,
    [string] $WpaExporterPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Assert-PathUnderRoot([string] $Path, [string] $Root, [string] $Label) {
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must remain under the native performance evidence directory: $Path"
    }
}

function Get-RelativeChildPath([string] $Path, [string] $Root) {
    Assert-PathUnderRoot -Path $Path -Root $Root -Label 'Exported file'
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    return $Path.Substring($prefix.Length).Replace('\', '/')
}

$evidenceRoot = Resolve-RepositoryPath $EvidenceDirectory
if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) {
    throw "Native performance evidence directory was not found: $evidenceRoot"
}
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
}
& (Join-Path $PSScriptRoot 'verify-native-performance-evidence.ps1') `
    -EvidenceDirectory $evidenceRoot -ExpectedCommit $ExpectedCommit

$profilePath = Resolve-RepositoryPath $WpaProfilePath
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf) `
    -or [IO.Path]::GetExtension($profilePath) -ine '.wpaProfile') {
    throw "A WPA profile file is required: $profilePath"
}

if ([string]::IsNullOrWhiteSpace($WpaExporterPath)) {
    $command = Get-Command 'wpaexporter.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $WpaExporterPath = $command.Source
    }
    else {
        $candidate = 'C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\wpaexporter.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $WpaExporterPath = $candidate
        }
    }
}
if ([string]::IsNullOrWhiteSpace($WpaExporterPath)) {
    throw 'wpaexporter.exe was not found. Install the Windows Performance Toolkit.'
}
$exporterPath = [IO.Path]::GetFullPath($WpaExporterPath)
if (-not (Test-Path -LiteralPath $exporterPath -PathType Leaf)) {
    throw "WPA exporter was not found: $exporterPath"
}

$rawManifestPath = Join-Path $evidenceRoot 'native-performance-evidence.json'
$rawManifest = Get-Content -LiteralPath $rawManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$tracePath = Join-Path $evidenceRoot ([string]$rawManifest.trace_file)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $outputRoot = Join-Path $evidenceRoot 'wpa-export'
}
else {
    $outputRoot = Resolve-RepositoryPath $OutputDirectory
}
Assert-PathUnderRoot -Path $outputRoot -Root $evidenceRoot -Label 'Output directory'
if (Test-Path -LiteralPath $outputRoot) {
    throw "Native performance export directory already exists: $outputRoot"
}
$outputParent = Split-Path -Parent $outputRoot
[IO.Directory]::CreateDirectory($outputParent) | Out-Null
$stage = Join-Path $outputParent ('.wpa-export-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null

try {
    & $exporterPath -i $tracePath -profile $profilePath `
        -outputfolder $stage -outputformat CSV
    $exportSucceeded = $?
    $exportExitCode = if (Test-Path Variable:LASTEXITCODE) {
        $LASTEXITCODE
    } else { 0 }
    if (-not $exportSucceeded -or $exportExitCode -ne 0) {
        throw "WPA exporter failed with exit code $exportExitCode."
    }

    $tables = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
        Where-Object { $_.Extension -in @('.csv', '.xml') })
    if ($tables.Count -eq 0) {
        throw 'WPA exporter produced no CSV or XML tables.'
    }
    foreach ($table in $tables) {
        if ($table.Length -le 0) {
            throw "WPA exporter produced an empty table: $($table.FullName)"
        }
    }

    $profileCopyPath = Join-Path $stage 'analysis.wpaProfile'
    Copy-Item -LiteralPath $profilePath -Destination $profileCopyPath
    $manifestPath = Join-Path $stage 'native-performance-export.json'
    $manifest = [ordered]@{
        schema_version = 1
        classification = 'native_performance_wpa_export'
        generated_at = [DateTimeOffset]::UtcNow.ToString('O')
        source_commit = $ExpectedCommit.Trim().ToLowerInvariant()
        raw_manifest_sha256 = (Get-FileHash -LiteralPath $rawManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        trace_sha256 = ([string]$rawManifest.trace_sha256).ToLowerInvariant()
        exporter_file = [IO.Path]::GetFileName($exporterPath)
        exporter_version = (Get-Item -LiteralPath $exporterPath).VersionInfo.FileVersion
        profile_file = 'analysis.wpaProfile'
        profile_sha256 = (Get-FileHash -LiteralPath $profileCopyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        output_format = 'CSV'
        exported_table_count = $tables.Count
        exported_tables = @($tables | Sort-Object FullName | ForEach-Object {
            [ordered]@{
                relative_path = Get-RelativeChildPath -Path $_.FullName -Root $stage
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                size_bytes = $_.Length
            }
        })
        analysis_status = 'pending_review'
        release_gate_passed = $false
    }
    Write-NewJsonFileAtomically -Value $manifest -Path $manifestPath -Depth 8 `
        -Label 'Native performance WPA export manifest'
    [IO.Directory]::Move($stage, $outputRoot)
}
catch {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    throw
}

Write-Host "WPA tables exported: $outputRoot"
Write-Host 'Analysis status: pending_review; release gate remains blocked.'
