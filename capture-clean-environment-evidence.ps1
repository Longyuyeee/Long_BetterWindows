#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture first-run and release-package evidence in a genuinely clean Windows user environment.
.DESCRIPTION
  The script never claims that the current user is clean automatically. The operator must make
  that assertion explicitly, and a different reviewer must still approve the interactive checks.
#>
param(
    [Parameter(Mandatory=$true)] [string] $ReleaseZip,
    [Parameter(Mandatory=$true)] [string] $ReleaseManifest,
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [Parameter(Mandatory=$true)] [string] $EnvironmentLabel,
    [switch] $ConfirmCleanUserEnvironment,
    [ValidateRange(10,60)] [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
if (-not $ConfirmCleanUserEnvironment) {
    throw 'ConfirmCleanUserEnvironment is required and must only be used in a new Windows user or disposable VM.'
}
if ([string]::IsNullOrWhiteSpace($EnvironmentLabel)) { throw 'EnvironmentLabel must not be empty.' }
if (-not [Environment]::UserInteractive) { throw 'An interactive Windows desktop session is required.' }
if ($null -eq (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    throw 'Windows Explorer is not running; tray and global-hotkey review would not be meaningful.'
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$zipPath = [IO.Path]::GetFullPath($ReleaseZip)
$releaseManifestPath = [IO.Path]::GetFullPath($ReleaseManifest)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw "Release ZIP was not found: $zipPath" }
if (-not (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf)) {
    throw "Release manifest was not found: $releaseManifestPath"
}
if (Test-Path -LiteralPath $outputRoot) { throw "Clean-environment evidence output already exists: $outputRoot" }
if (Get-Process -Name 'LongBetterWindows.Host' -ErrorAction SilentlyContinue) {
    throw 'LongBetterWindows.Host is already running. Start capture before the first launch.'
}

$release = Get-Content -LiteralPath $releaseManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$zipName = [IO.Path]::GetFileName($zipPath)
$package = @($release.packages | Where-Object { $_.file -eq $zipName })
if ($package.Count -ne 1) { throw "Release manifest does not identify exactly one package named $zipName." }
$actualZipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualZipHash -ne ([string]$package[0].sha256).ToLowerInvariant()) {
    throw 'Release ZIP hash does not match the release manifest.'
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$capturedReleaseManifestPath = Join-Path $outputRoot 'release-manifest.json'
Copy-Item -LiteralPath $releaseManifestPath -Destination $capturedReleaseManifestPath
$installRoot = Join-Path $outputRoot 'candidate-install'
Expand-Archive -LiteralPath $zipPath -DestinationPath $installRoot
$executable = Join-Path $installRoot 'LongBetterWindows.Host.exe'
$pluginsRoot = Join-Path $installRoot 'Plugins'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Candidate host executable is missing.' }

$pluginManifests = @(Get-ChildItem -LiteralPath $pluginsRoot -Filter manifest.json -File -Recurse)
$pluginDocuments = @($pluginManifests | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
})
$pluginIds = @($pluginDocuments | ForEach-Object { [string]$_.id })
$uniquePluginIds = @($pluginIds | Sort-Object -Unique)
$commandCount = @($pluginDocuments | ForEach-Object { @($_.commands) }).Count
if ($pluginManifests.Count -ne 25 -or $uniquePluginIds.Count -ne 25 -or $commandCount -ne 42) {
    throw "Candidate inventory mismatch. Manifests=$($pluginManifests.Count), unique IDs=$($uniquePluginIds.Count), commands=$commandCount."
}

$commandLogPath = Join-Path $outputRoot 'command-smoke.log'
$commandProcess = Start-Process -FilePath $executable -ArgumentList @(
    '--plugins-dir', $pluginsRoot,
    '--run-command', 'com.long.base64:base64.encode',
    '--command-text', 'LongBetterWindows-clean-environment',
    '--exit-after-command'
) -WorkingDirectory $outputRoot -PassThru -Wait -RedirectStandardOutput $commandLogPath
if ($commandProcess.ExitCode -ne 0) { throw "Release command smoke failed with exit code $($commandProcess.ExitCode)." }

$uiSmokeRoot = Join-Path $outputRoot 'desktop-ui-smoke'
& (Join-Path $repoRoot 'run-desktop-ui-smoke.ps1') -OutputDirectory $uiSmokeRoot `
    -ReleaseDirectory $installRoot -TimeoutSeconds $TimeoutSeconds
$uiReportPath = Join-Path $uiSmokeRoot 'desktop-ui-smoke.json'
$uiLogPath = Join-Path $uiSmokeRoot 'desktop-ui-smoke.log'
$uiReport = Get-Content -LiteralPath $uiReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [bool]$uiReport.passed) { throw 'Release-package desktop UI report is not passing.' }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$os = Get-CimInstance Win32_OperatingSystem
$manifest = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'clean_windows_release_evidence'
    environment = [ordered]@{
        label = $EnvironmentLabel.Trim()
        machine = [Environment]::MachineName
        user = [Environment]::UserName
        user_sid = $identity.User.Value
        os_caption = [string]$os.Caption
        os_version = [string]$os.Version
        os_build = [string]$os.BuildNumber
        interactive = [Environment]::UserInteractive
        explorer_detected = $true
        operator_asserted_clean_user = $true
    }
    release = [ordered]@{
        version = [string]$release.version
        package_file = $zipName
        package_kind = [string]$package[0].kind
        package_sha256 = $actualZipHash
        signed = [bool]$release.signed
        release_eligible = [bool]$release.release_eligible
        release_manifest = [ordered]@{
            file = 'release-manifest.json'
            sha256 = (Get-FileHash $capturedReleaseManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        manifests = $pluginManifests.Count
        unique_plugin_ids = $uniquePluginIds.Count
        commands = $commandCount
    }
    automated_checks = [ordered]@{
        passed = $true
        first_process_command_exit_code = $commandProcess.ExitCode
        desktop_ui_passed = [bool]$uiReport.passed
        desktop_ui_report = [ordered]@{
            file = 'desktop-ui-smoke/desktop-ui-smoke.json'
            sha256 = (Get-FileHash $uiReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        desktop_ui_log = [ordered]@{
            file = 'desktop-ui-smoke/desktop-ui-smoke.log'
            sha256 = (Get-FileHash $uiLogPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        command_log = [ordered]@{
            file = 'command-smoke.log'
            sha256 = (Get-FileHash $commandLogPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    human_review = [ordered]@{
        status = 'pending'
        reviewer = $null
        reviewed_at = $null
        notes = $null
        checklist = [ordered]@{
            first_start = $false
            tray_icon = $false
            global_hotkey = $false
            webview_runtime = $false
            parallel_upgrade_data_preserved = $false
            rollback_to_previous_version = $false
            uninstall_integrations_removed = $false
        }
    }
}
$evidencePath = Join-Path $outputRoot 'clean-environment-evidence.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidencePath -Encoding UTF8
Write-Output 'Automated clean-environment release checks passed; independent human review remains pending.'
Write-Output "Evidence: $evidencePath"
