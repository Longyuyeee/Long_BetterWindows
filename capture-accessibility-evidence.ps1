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
    [ValidateSet('None','Narrator','NVDA')] [string] $ScreenReader = 'None',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
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
if ($NoBuild) { $smokeParameters.NoBuild = $true }
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
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'physical_accessibility_evidence'
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
            screen_reader_announcements = $null
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
