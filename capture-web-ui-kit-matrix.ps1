#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture the Long Web UI Kit reference plugin across its engineering visual matrix.
.NOTES
  Engineering captures do not replace physical DPI or assistive-technology validation.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateSet('light','dark')] [string[]] $Themes = @('light','dark'),
    [ValidateSet('normal','high-contrast','reduced-motion','combined')]
    [string[]] $AccessibilityModes = @('normal','high-contrast','reduced-motion','combined'),
    [ValidateSet(920,640)] [int[]] $Widths = @(920,640),
    [ValidateRange(520,2160)] [int] $Height = 720,
    [ValidateRange(100,10000)] [int] $CaptureDelayMilliseconds = 900,
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
    throw "Web UI Kit matrix output directory already exists: $outputRoot"
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
    if ($LASTEXITCODE -ne 0) { throw 'Web UI Kit matrix Release build failed.' }
}
$executable = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }

$pluginId = 'com.long.reference-web-ui-kit'
$pluginsDirectory = Join-Path $repoRoot 'samples'
$manifest = Get-Content -LiteralPath (Join-Path $pluginsDirectory 'LongWebUiKitPreview\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$captures = @()
foreach ($mode in $AccessibilityModes) {
    foreach ($theme in $Themes) {
        foreach ($width in $Widths) {
            $fileName = "$theme-$mode-$width.png"
            $capturePath = Join-Path $outputRoot $fileName
            $arguments = @(
                '--plugins-dir', $pluginsDirectory,
                '--theme', $theme,
                '--quality-open-plugin-runtime', $pluginId,
                '--quality-capture', $capturePath,
                '--quality-capture-view', 'plugin',
                '--quality-width', $width.ToString(),
                '--quality-height', $Height.ToString(),
                '--quality-capture-delay-ms', $CaptureDelayMilliseconds.ToString()
            )
            $expectedHighContrast = $mode -in @('high-contrast','combined')
            $expectedReducedMotion = $mode -in @('reduced-motion','combined')
            if ($expectedHighContrast) { $arguments += '--quality-high-contrast' }
            if ($expectedReducedMotion) { $arguments += '--quality-reduce-motion' }

            $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $outputRoot -PassThru
            if (-not $process.WaitForExit(30000)) {
                Stop-Process -Id $process.Id -Force
                throw "Web UI Kit capture timed out: $fileName"
            }
            if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $capturePath)) {
                throw "Web UI Kit capture failed with exit code $($process.ExitCode): $fileName"
            }

            $metadata = Get-Content -LiteralPath ($capturePath + '.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([bool]$metadata.high_contrast -ne $expectedHighContrast -or
                [bool]$metadata.reduced_motion -ne $expectedReducedMotion) {
                throw "Accessibility metadata mismatch: $fileName"
            }
            $captures += [ordered]@{
                file = $fileName
                theme = $theme
                accessibility_mode = $mode
                width = $width
                height = $Height
                high_contrast = [bool]$metadata.high_contrast
                reduced_motion = [bool]$metadata.reduced_motion
                pixel_width = $metadata.pixel_width
                pixel_height = $metadata.pixel_height
                sha256 = (Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash.ToLowerInvariant()
                bytes = (Get-Item -LiteralPath $capturePath).Length
            }
        }
    }
}

$matrix = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'engineering_web_ui_kit_render_matrix'
    plugin_id = $pluginId
    ui_kit_version = $manifest.min_ui_kit_version
    physical_device_matrix_required = $true
    captures = $captures
}
Write-NewJsonFileAtomically `
    -Value $matrix `
    -Path (Join-Path $outputRoot 'web-ui-kit-matrix.json') `
    -Depth 6 `
    -Label 'Web UI Kit matrix manifest'
Write-Output "Web UI Kit matrix captured: $($captures.Count) images"
Write-Output "Output: $outputRoot"
