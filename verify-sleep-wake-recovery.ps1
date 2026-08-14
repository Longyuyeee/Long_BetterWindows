param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [ValidateRange(2, 45)]
    [int]$TimeoutMinutes = 20,
    [switch]$TriggerSleep
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
    throw "Physical sleep/wake evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/sleep-wake-recovery-$stamp/sleep-wake-recovery.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
$readyFile = [IO.Path]::ChangeExtension($outputFile, ".ready.json")
$outputExists = Test-Path -LiteralPath $outputFile
$readyExists = Test-Path -LiteralPath $readyFile
if ($outputExists -or $readyExists) {
    throw "Physical sleep/wake recovery output already exists."
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-power-recovery-report", $outputFile,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
try {
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $readyFile -PathType Leaf)) {
        if ($process.HasExited) {
            throw "Sleep/wake recovery host exited before readiness."
        }
        if ([DateTime]::UtcNow -ge $readyDeadline) {
            throw "Sleep/wake recovery host did not become ready within 45 seconds."
        }
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    Write-Host "Sleep/wake recovery probe is ready."
    Write-Host "Put Windows to sleep, then wake and unlock this computer."
    if ($TriggerSleep) {
        Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class LongPowerNative {
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetSuspendState(
        bool hibernate,
        bool forceCritical,
        bool disableWakeEvent);
}
"@
        if (-not [LongPowerNative]::SetSuspendState($false, $false, $false)) {
            throw "SetSuspendState failed. Use the Windows power menu to sleep manually."
        }
    }

    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        throw "Sleep/wake recovery probe timed out after $TimeoutMinutes minutes."
    }
    if ($process.ExitCode -ne 0) {
        throw "Sleep/wake recovery probe failed with exit code $($process.ExitCode)."
    }
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null
    }
    $process.Dispose()
}

if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
    throw "Sleep/wake recovery report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$resumeKind = [string]$report.resume_kind
$passed =
    [int]$report.schema_version -eq 1 -and
    [string]$report.classification -eq "physical_sleep_wake_recovery" -and
    [string]$report.event_source -eq "WM_POWERBROADCAST" -and
    [bool]$report.passed -and
    [bool]$report.unavailable_after_suspend -and
    [bool]$report.restored -and
    [bool]$report.restored_host_state -and
    [bool]$report.identity_preserved -and
    [bool]$report.surface_preserved -and
    [bool]$report.cleanup_passed -and
    $resumeKind -in @("ResumedFromSuspend", "ResumedAutomatically") -and
    [DateTimeOffset]$report.resumed_at -ge [DateTimeOffset]$report.suspended_at
if (-not $passed) {
    throw "Sleep/wake recovery report did not satisfy the physical gate."
}

Write-Host "Sleep/wake recovery report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
Write-Host "Physical sleep/wake recovery: passed"
