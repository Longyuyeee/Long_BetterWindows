param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [ValidateRange(2, 30)]
    [int]$TimeoutMinutes = 15,
    [switch]$TriggerLock
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
    throw "Physical session evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/session-recovery-$stamp/session-recovery.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
$readyFile = [IO.Path]::ChangeExtension($outputFile, ".ready.json")
if (Test-Path -LiteralPath $outputFile -or
    Test-Path -LiteralPath $readyFile) {
    throw "Physical session recovery output already exists."
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-session-recovery-report", $outputFile,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
try {
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath $readyFile -PathType Leaf)) {
        if ($process.HasExited) {
            throw "Session recovery host exited before readiness."
        }
        if ([DateTime]::UtcNow -ge $readyDeadline) {
            throw "Session recovery host did not become ready within 45 seconds."
        }
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    Write-Host "Session recovery probe is ready."
    Write-Host "Unlock Windows after the lock screen appears."
    if ($TriggerLock) {
        Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class LongSessionLockNative {
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();
}
"@
        if (-not [LongSessionLockNative]::LockWorkStation()) {
            throw "LockWorkStation failed."
        }
    }

    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        throw "Session recovery probe timed out after $TimeoutMinutes minutes."
    }
    if ($process.ExitCode -ne 0) {
        throw "Session recovery probe failed with exit code $($process.ExitCode)."
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
    throw "Session recovery report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$passed =
    [int]$report.schema_version -eq 1 -and
    [string]$report.classification -eq "physical_session_lock_recovery" -and
    [bool]$report.passed -and
    [bool]$report.unavailable_after_lock -and
    [bool]$report.restored -and
    [bool]$report.restored_host_state -and
    [bool]$report.identity_preserved -and
    [bool]$report.surface_preserved -and
    [bool]$report.cleanup_passed -and
    [DateTimeOffset]$report.unlocked_at -ge [DateTimeOffset]$report.locked_at
if (-not $passed) {
    throw "Session recovery report did not satisfy the physical gate."
}

Write-Host "Session recovery report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
Write-Host "Lock/unlock recovery: passed"
