param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [string]$TargetMonitorDeviceName = "",
    [ValidateRange(2, 20)]
    [int]$TimeoutMinutes = 10
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

$hostRoot = Resolve-RepositoryPath $HostDirectory
$hostExecutable = Join-Path $hostRoot "LongBetterWindows.Host.exe"
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "Release host executable was not found: $hostExecutable"
}
if (@(Get-Process -Name "LongBetterWindows.Host" `
        -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "Close existing LongBetterWindows.Host processes before this probe."
}

$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Unable to resolve the source commit."
}
$trackedStatus = @(& git -C $PSScriptRoot status `
    --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedStatus.Count -gt 0) {
    throw "Physical display topology evidence requires a clean tracked source tree."
}

Add-Type -AssemblyName System.Windows.Forms
$screens = @([System.Windows.Forms.Screen]::AllScreens)
if ($screens.Count -lt 2) {
    throw "Physical display topology recovery requires at least two active monitors."
}
if ([string]::IsNullOrWhiteSpace($TargetMonitorDeviceName)) {
    $target = $screens | Where-Object { -not $_.Primary } | Select-Object -First 1
    if ($null -eq $target) {
        $target = $screens | Select-Object -Last 1
    }
    $TargetMonitorDeviceName = $target.DeviceName
}
if ($TargetMonitorDeviceName -notin @($screens | ForEach-Object DeviceName)) {
    throw "Target monitor is not active: $TargetMonitorDeviceName"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/display-topology-$stamp/display-topology.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
$readyFile = [IO.Path]::ChangeExtension($outputFile, ".ready.json")
$reducedReadyFile = [IO.Path]::ChangeExtension(
    $outputFile,
    ".reduced.ready.json")
if ((Test-Path -LiteralPath $outputFile) `
    -or (Test-Path -LiteralPath $readyFile) `
    -or (Test-Path -LiteralPath $reducedReadyFile)) {
    throw "Physical display topology output already exists."
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$displaySwitch = Join-Path $env:WINDIR "System32\DisplaySwitch.exe"
if (-not (Test-Path -LiteralPath $displaySwitch -PathType Leaf)) {
    throw "DisplaySwitch.exe was not found: $displaySwitch"
}

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-display-topology-report", $outputFile,
        "--quality-monitor-device", $TargetMonitorDeviceName,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
$reducedRequested = $false
$restoreRequested = $false
try {
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $readyFile -PathType Leaf)) {
        if ($process.HasExited) {
            throw "Display topology host exited before readiness."
        }
        if ([DateTime]::UtcNow -ge $readyDeadline) {
            throw "Display topology host did not become ready within 45 seconds."
        }
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    Write-Host "Display topology probe is ready on $TargetMonitorDeviceName."
    Write-Host "Reducing Windows to the internal display."
    $switch = Start-Process -FilePath $displaySwitch `
        -ArgumentList "/internal" -WindowStyle Hidden -Wait -PassThru
    if ($switch.ExitCode -ne 0) {
        throw "DisplaySwitch /internal failed with exit code $($switch.ExitCode)."
    }
    $reducedRequested = $true

    $reducedDeadline = [DateTime]::UtcNow.AddMinutes(2)
    while (-not (Test-Path -LiteralPath $reducedReadyFile -PathType Leaf)) {
        if ($process.HasExited) {
            throw "Display topology host exited before reduced topology readiness."
        }
        if ([DateTime]::UtcNow -ge $reducedDeadline) {
            throw "Reduced display topology was not confirmed within 2 minutes."
        }
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    Write-Host "Reduced topology confirmed. Restoring extended displays."
    $switch = Start-Process -FilePath $displaySwitch `
        -ArgumentList "/extend" -WindowStyle Hidden -Wait -PassThru
    if ($switch.ExitCode -ne 0) {
        throw "DisplaySwitch /extend failed with exit code $($switch.ExitCode)."
    }
    $restoreRequested = $true

    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        throw "Display topology recovery probe timed out after $TimeoutMinutes minutes."
    }
    if ($process.ExitCode -ne 0) {
        throw "Display topology recovery probe failed with exit code $($process.ExitCode)."
    }
}
finally {
    if ($reducedRequested -and -not $restoreRequested) {
        Start-Process -FilePath $displaySwitch -ArgumentList "/extend" `
            -WindowStyle Hidden -Wait | Out-Null
    }
    $process.Refresh()
    if (-not $process.HasExited) {
        & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null
    }
    $process.Dispose()
}

if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
    throw "Display topology recovery report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$passed =
    [int]$report.schema_version -eq 1 -and
    [string]$report.classification -eq "physical_display_topology_recovery" -and
    [string]$report.event_source -eq "WM_DISPLAYCHANGE" -and
    [bool]$report.passed -and
    [int]$report.initial_monitors.Count -ge 2 -and
    [int]$report.reduced_monitors.Count -eq 1 -and
    [int]$report.restored_monitors.Count -ge 2 -and
    [int]$report.display_event_count -ge 2 -and
    [bool]$report.topology_restored -and
    [bool]$report.reduced_window.intersects_work_area -and
    [bool]$report.restored_window.intersects_work_area -and
    [bool]$report.reduced_host_state -and
    [bool]$report.restored_host_state -and
    [bool]$report.identity_preserved -and
    [bool]$report.surface_preserved -and
    [bool]$report.cleanup_passed
if (-not $passed) {
    throw "Display topology report did not satisfy the physical gate."
}

Write-Host "Display topology report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
Write-Host "Physical display topology recovery: passed"
