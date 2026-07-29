#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture desktop UI evidence under real Windows accessibility settings.
.DESCRIPTION
  Does not change system settings. The requested profile must already be active.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [Parameter(Mandatory=$true)]
    [ValidateSet('high_contrast','reduced_motion','combined')] [string] $ExpectedProfile,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [ValidateSet('None','Narrator','NVDA')] [string] $ScreenReader = 'None',
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
if ($LASTEXITCODE -ne 0) { throw 'Accessibility evidence requires a clean tracked source tree.' }
if ($NoBuild) { throw 'Formal accessibility evidence must rebuild the expected source commit.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Accessibility evidence output already exists: $outputRoot"
}

Add-Type -AssemblyName PresentationFramework
$actualHighContrast = [bool][System.Windows.SystemParameters]::HighContrast
$clientAreaAnimation = [bool][System.Windows.SystemParameters]::ClientAreaAnimation
$actualReducedMotion = -not $clientAreaAnimation
$profileMatches = switch ($ExpectedProfile) {
    'high_contrast' { $actualHighContrast -and -not $actualReducedMotion }
    'reduced_motion' { -not $actualHighContrast -and $actualReducedMotion }
    'combined' { $actualHighContrast -and $actualReducedMotion }
}
if (-not $profileMatches) {
    throw "Windows accessibility settings do not match $ExpectedProfile. HighContrast=$actualHighContrast, ReducedMotion=$actualReducedMotion."
}

$readerProcessName = switch ($ScreenReader) {
    'Narrator' { 'Narrator' }
    'NVDA' { 'nvda' }
    default { $null }
}
$readerDetected = $false
if ($null -ne $readerProcessName) {
    $readerDetected = $null -ne (Get-Process -Name $readerProcessName -ErrorAction SilentlyContinue)
    if (-not $readerDetected) {
        throw "Requested screen reader process is not running: $ScreenReader"
    }
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$smokeRoot = Join-Path $outputRoot 'desktop-ui-smoke'
$smokeScript = Join-Path $repoRoot 'run-desktop-ui-smoke.ps1'
$smokeParameters = @{ OutputDirectory = $smokeRoot }
$smokeOutput = & $smokeScript @smokeParameters
$smokeOutput | ForEach-Object { Write-Host $_ }

$reportPath = Join-Path $smokeRoot 'desktop-ui-smoke.json'
if (-not (Test-Path -LiteralPath $reportPath)) { throw 'Desktop UI smoke report was not generated.' }
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [bool]$report.passed -or @($report.accessibility_modes).Count -ne 3 `
    -or @($report.automation_semantics.psobject.Properties).Count -lt 4) {
    throw 'Desktop UI smoke did not satisfy the accessibility evidence gate.'
}

$logPath = Join-Path $smokeRoot 'desktop-ui-smoke.log'
$manifest = [ordered]@{
    schema_version = 2
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'physical_accessibility_evidence'
    source_commit = $expectedCommit
    expected_profile = $ExpectedProfile
    automated_checks_passed = $true
    windows_settings = [ordered]@{
        high_contrast = $actualHighContrast
        client_area_animation = $clientAreaAnimation
        reduced_motion = $actualReducedMotion
    }
    screen_reader = [ordered]@{
        name = $ScreenReader
        process_name = $readerProcessName
        process_detected = $readerDetected
    }
    desktop_ui_report = [ordered]@{
        file = 'desktop-ui-smoke/desktop-ui-smoke.json'
        sha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        passed = [bool]$report.passed
        semantic_group_count = @($report.automation_semantics.psobject.Properties).Count
        accessibility_mode_count = @($report.accessibility_modes).Count
    }
    desktop_ui_log = [ordered]@{
        file = 'desktop-ui-smoke/desktop-ui-smoke.log'
        sha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    human_review = [ordered]@{
        status = 'pending'
        reviewer = $null
        reviewed_at = $null
        notes = $null
        checklist = [ordered]@{
            keyboard_navigation = $false
            focus_visibility = $false
            motion_behavior = $false
            management_destination_tab_order = $false
            management_destination_activation = $false
            management_module_close_mru = $false
            screen_reader_announcements = $null
            management_close_announcements = $null
        }
    }
    environment = [ordered]@{
        os_version = [Environment]::OSVersion.VersionString
        process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
}
$manifestPath = Join-Path $outputRoot 'accessibility-evidence.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Accessibility evidence captured for profile: $ExpectedProfile"
Write-Output "Screen reader: $ScreenReader (detected=$readerDetected)"
Write-Output "Manifest: $manifestPath"
