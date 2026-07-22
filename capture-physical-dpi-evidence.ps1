#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture Long UI evidence on a display whose physical Windows scale matches the requested value.
.DESCRIPTION
  This command rejects mismatched monitor DPI. It records automated evidence only unless
  -ApproveAfterVisualReview is explicitly supplied with a reviewer and review notes.
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
    [ValidateRange(100,10000)] [int] $CaptureDelayMilliseconds = 1500,
    [switch] $ApproveAfterVisualReview,
    [string] $Reviewer,
    [string] $ReviewNotes,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
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
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Physical DPI evidence output directory already exists: $outputRoot"
}
if ($ApproveAfterVisualReview) {
    if ([string]::IsNullOrWhiteSpace($Reviewer)) {
        throw 'Reviewer is required when approving physical DPI evidence.'
    }
    if ([string]::IsNullOrWhiteSpace($ReviewNotes) -or $ReviewNotes.Trim().Length -lt 8) {
        throw 'ReviewNotes must contain at least 8 characters when approving evidence.'
    }
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
            $arguments += @('--run-command', $PluginCommandKey)
        }

        $process = Start-Process -FilePath $executable -ArgumentList $arguments `
            -WorkingDirectory $outputRoot -PassThru
        if (-not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -Force
            throw "Physical DPI capture timed out: $fileName"
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

$approved = [bool]$ApproveAfterVisualReview
$manifest = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'physical_device_dpi_evidence'
    source_commit = $expectedCommit
    expected_scale_percent = $ExpectedScalePercent
    expected_dpi = $expectedDpi
    automated_checks_passed = $true
    required_release_matrix_member = ($ExpectedScalePercent -in @(100,125,150,200))
    human_review = [ordered]@{
        status = if ($approved) { 'approved' } else { 'pending' }
        reviewer = if ($approved) { $Reviewer.Trim() } else { $null }
        reviewed_at = if ($approved) { [DateTimeOffset]::UtcNow.ToString('O') } else { $null }
        notes = if ($approved) { $ReviewNotes.Trim() } else { $null }
        checklist = [ordered]@{
            no_clipping_or_overflow = $approved
            text_and_icons_are_sharp = $approved
            keyboard_focus_is_visible = $approved
            light_and_dark_themes_are_consistent = $approved
            web_plugin_content_is_visible = $approved
        }
    }
    environment = [ordered]@{
        os_version = [Environment]::OSVersion.VersionString
        process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
    captures = $captures
}
$manifestPath = Join-Path $outputRoot 'physical-dpi-evidence.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output "Physical DPI evidence captured: $($captures.Count) images at $ExpectedScalePercent%."
Write-Output "Human review: $($manifest.human_review.status)"
Write-Output "Manifest: $manifestPath"
