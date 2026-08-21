#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture Long UI evidence on a display whose physical Windows scale matches the requested value.
.DESCRIPTION
  This command rejects mismatched monitor DPI and writes immutable automated evidence.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [Parameter(Mandatory=$true)]
    [ValidateSet(100,125,150,200,250)] [int] $ExpectedScalePercent,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [ValidateSet('light','dark')] [string[]] $Themes = @('light','dark'),
    [ValidateSet('main','market','palette','plugin')]
    [string[]] $Views = @('main','market','palette','plugin'),
    [string] $PluginCommandKey = 'com.long.url-toolkit:url.encode',
    [string] $PluginCommandText = 'LongAssistantDpiReview',
    [string] $MonitorDeviceName,
    [ValidateRange(100,10000)] [int] $CaptureDelayMilliseconds = 1500,
    [ValidateRange(30,180)] [int] $ProcessTimeoutSeconds = 90,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
$actualCommit = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw 'Repository HEAD does not match ExpectedSourceCommit.'
}
& git -C $repoRoot diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Physical DPI evidence requires a clean tracked source tree.' }
if ($NoBuild) { throw 'Formal physical DPI evidence must rebuild the expected source commit.' }
if ('main' -notin $Views) {
    throw 'Physical DPI evidence must include the main management-center view.'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Physical DPI evidence output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw 'dotnet CLI was not found.' }
    $dotnet = $command.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
& $dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Physical DPI evidence Release build failed.' }
$executable = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }

$expectedDpi = $ExpectedScalePercent * 96.0 / 100.0
$captures = @()
foreach ($theme in $Themes) {
    foreach ($view in $Views) {
        $fileName = "$theme-physical-$ExpectedScalePercent-$view.png"
        $capturePath = Join-Path $outputRoot $fileName
        $arguments = @(
            '--theme', $theme,
            '--quality-capture', $capturePath,
            '--quality-capture-view', $view,
            '--quality-render-dpi', ([int]$expectedDpi).ToString(),
            '--quality-capture-delay-ms', $CaptureDelayMilliseconds.ToString()
        )
        if ($view -eq 'market') { $arguments += '--quality-open-market' }
        if ($view -eq 'palette') { $arguments += '--quality-open-palette' }
        if ($view -eq 'plugin') {
            $arguments += @(
                '--run-command', $PluginCommandKey,
                '--command-text', $PluginCommandText
            )
        }
        if (-not [string]::IsNullOrWhiteSpace($MonitorDeviceName)) {
            $arguments += @('--quality-monitor-device', $MonitorDeviceName.Trim())
        }

        $process = Start-Process -FilePath $executable -ArgumentList $arguments `
            -WorkingDirectory $outputRoot -PassThru
        if (-not $process.WaitForExit($ProcessTimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force
            throw "Physical DPI capture timed out after $ProcessTimeoutSeconds seconds: $fileName"
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $capturePath)) {
            throw "Physical DPI capture failed with exit code $($process.ExitCode): $fileName"
        }

        $metadataPath = $capturePath + '.json'
        $metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $actualDpi = [double]$metadata.actual_monitor_dpi
        if ([Math]::Abs($actualDpi - $expectedDpi) -gt 0.6) {
            throw "Physical monitor DPI mismatch. Expected $expectedDpi DPI ($ExpectedScalePercent%), captured $actualDpi DPI. Move Long to the correct display and retry."
        }
        $actualMonitorDeviceName = [string]$metadata.actual_monitor_device_name
        if ([string]::IsNullOrWhiteSpace($actualMonitorDeviceName)) {
            throw "Capture did not report its physical monitor device: $fileName"
        }
        if (-not [string]::IsNullOrWhiteSpace($MonitorDeviceName) `
            -and -not [string]::Equals(
                $actualMonitorDeviceName,
                $MonitorDeviceName.Trim(),
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Physical monitor mismatch. Expected $MonitorDeviceName, captured $actualMonitorDeviceName."
        }
        $expectedKind = if ($view -eq 'plugin') { 'webview_preview' } else { 'wpf_render_target' }
        if ($metadata.capture_kind -ne $expectedKind) {
            throw "Unexpected capture kind for $view. Expected $expectedKind, got $($metadata.capture_kind)."
        }
        $captures += [ordered]@{
            file = $fileName
            metadata_file = [IO.Path]::GetFileName($metadataPath)
            theme = $theme
            view = $view
            actual_monitor_dpi = $actualDpi
            actual_scale_percent = [Math]::Round($actualDpi / 96.0 * 100.0, 1)
            actual_monitor_device_name = $actualMonitorDeviceName
            actual_monitor_bounds = $metadata.actual_monitor_bounds
            actual_monitor_work_area = $metadata.actual_monitor_work_area
            actual_monitor_primary = [bool]$metadata.actual_monitor_primary
            capture_kind = $metadata.capture_kind
            logical_width = $metadata.logical_width
            logical_height = $metadata.logical_height
            pixel_width = $metadata.pixel_width
            pixel_height = $metadata.pixel_height
            sha256 = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash.ToLowerInvariant()
            metadata_sha256 = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
            bytes = (Get-Item -LiteralPath $capturePath).Length
        }
    }
}

$manifest = [ordered]@{
    schema_version = 3
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'automated_physical_device_dpi_evidence'
    source_commit = $expectedCommit
    expected_scale_percent = $ExpectedScalePercent
    expected_dpi = $expectedDpi
    automated_checks_passed = $true
    required_release_matrix_member = ($ExpectedScalePercent -in @(100,125,150,200))
    environment = [ordered]@{
        os_version = [Environment]::OSVersion.VersionString
        process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        requested_monitor_device_name = if ([string]::IsNullOrWhiteSpace($MonitorDeviceName)) { $null } else { $MonitorDeviceName.Trim() }
    }
    captures = $captures
}
$manifestPath = Join-Path $outputRoot 'physical-dpi-evidence.json'
Write-NewJsonFileAtomically `
    -Value $manifest `
    -Path $manifestPath `
    -Depth 8 `
    -Label 'Physical DPI evidence manifest'

Write-Output "Physical DPI evidence captured: $($captures.Count) images at $ExpectedScalePercent%."
Write-Output "Manifest: $manifestPath"
