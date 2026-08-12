#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture a deterministic engineering render matrix for Long WPF views.
.NOTES
  Target render DPI is not a substitute for validation on physical monitors at each scale.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateSet('light','dark')] [string[]] $Themes = @('light','dark'),
    [ValidateSet(96,120,144,192)] [int[]] $RenderDpis = @(96,120,144,192),
    [ValidateSet('main','market','palette','super-panel','diagnostics','developer','settings')] [string[]] $Views = @('main','market','palette'),
    [ValidateSet('normal','high-contrast','reduced-motion','combined')]
    [string[]] $AccessibilityModes = @('normal'),
    [ValidateRange(640,3840)] [int] $CaptureWidth = 1120,
    [ValidateRange(480,2160)] [int] $CaptureHeight = 760,
    [ValidateRange(100,10000)] [int] $CaptureDelayMilliseconds = 700,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$evidenceIo = Join-Path $repoRoot 'release-evidence-io.ps1'
if (-not (Test-Path -LiteralPath $evidenceIo -PathType Leaf)) {
    throw "Release evidence writer was not found: $evidenceIo"
}
. $evidenceIo

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Visual matrix output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw 'dotnet CLI was not found.' }
    $dotnet = $command.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
if (-not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Visual matrix Release build failed.' }
}
$executable = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }

$captures = @()
foreach ($accessibilityMode in $AccessibilityModes) {
    foreach ($theme in $Themes) {
        foreach ($dpi in $RenderDpis) {
            foreach ($view in $Views) {
            $scale = [int][Math]::Round($dpi / 96 * 100)
            $modeSegment = if ($accessibilityMode -eq 'normal') { '' } else { "-$accessibilityMode" }
            $fileName = "$theme$modeSegment-render-$scale-$view.png"
            $capturePath = Join-Path $outputRoot $fileName
            $arguments = @(
                '--theme', $theme,
                '--quality-capture', $capturePath,
                '--quality-capture-view', $view,
                '--quality-render-dpi', $dpi.ToString(),
                '--quality-width', $CaptureWidth.ToString(),
                '--quality-height', $CaptureHeight.ToString(),
                '--quality-capture-delay-ms', $CaptureDelayMilliseconds.ToString()
            )
            if ($view -eq 'market') { $arguments += '--quality-open-market' }
            if ($view -eq 'palette') { $arguments += '--quality-open-palette' }
            if ($view -eq 'super-panel') { $arguments += '--quality-open-super-panel' }
            if ($accessibilityMode -in @('high-contrast','combined')) {
                $arguments += '--quality-high-contrast'
            }
            if ($accessibilityMode -in @('reduced-motion','combined')) {
                $arguments += '--quality-reduce-motion'
            }
            $process = Start-Process -FilePath $executable -ArgumentList $arguments `
                -WorkingDirectory $outputRoot -PassThru
            if (-not $process.WaitForExit(30000)) {
                Stop-Process -Id $process.Id -Force
                throw "Visual capture timed out: $fileName"
            }
            if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $capturePath)) {
                throw "Visual capture failed with exit code $($process.ExitCode): $fileName"
            }
            $metadata = Get-Content -LiteralPath ($capturePath + '.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            $captures += [ordered]@{
                file = $fileName
                theme = $theme
                view = $view
                accessibility_mode = $accessibilityMode
                render_dpi = $dpi
                actual_monitor_dpi = $metadata.actual_monitor_dpi
                high_contrast = $metadata.high_contrast
                reduced_motion = $metadata.reduced_motion
                logical_width = $metadata.logical_width
                logical_height = $metadata.logical_height
                pixel_width = $metadata.pixel_width
                pixel_height = $metadata.pixel_height
                sha256 = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash.ToLowerInvariant()
                bytes = (Get-Item -LiteralPath $capturePath).Length
            }
            }
        }
    }
}

$manifest = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = if (@($AccessibilityModes | Where-Object { $_ -ne 'normal' }).Count -gt 0) {
        'engineering_accessibility_render_matrix'
    } else {
        'engineering_render_matrix'
    }
    physical_device_matrix_required = $true
    captures = $captures
}
Write-NewJsonFileAtomically `
    -Value $manifest `
    -Path (Join-Path $outputRoot 'visual-matrix.json') `
    -Depth 6 `
    -Label 'Visual matrix manifest'
Write-Output "Visual matrix captured: $($captures.Count) images"
Write-Output "Output: $outputRoot"
