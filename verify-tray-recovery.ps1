param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [ValidateRange(30, 180)]
    [int]$TimeoutSeconds = 120
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
    throw "Tray recovery evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/tray-recovery-$stamp/tray-recovery.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
if (Test-Path -LiteralPath $outputFile) {
    throw "Tray recovery output already exists: $outputFile"
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-tray-recovery-report", $outputFile,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null
        throw "Tray recovery probe timed out after $TimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) {
        throw "Tray recovery probe failed with exit code $($process.ExitCode)."
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
    throw "Tray recovery report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$cycles = @($report.cycles)
$invalidCycles = @($cycles | Where-Object {
    -not [bool]$_.close_intercepted -or
    -not [bool]$_.hidden -or
    -not [bool]$_.hidden_host_state -or
    -not [bool]$_.primary_action_handled -or
    -not [bool]$_.restored -or
    -not [bool]$_.restored_host_state -or
    -not [bool]$_.passed
})
$passed =
    [int]$report.schema_version -eq 1 -and
    [bool]$report.passed -and
    [bool]$report.cleanup_passed -and
    [int]$report.warm_baseline_cycle -eq 1 -and
    [bool]$report.growth.passed -and
    [bool]$report.resource_trend.passed -and
    [int]$report.cycle_count -eq 8 -and
    $cycles.Count -eq 8 -and
    $invalidCycles.Count -eq 0
if (-not $passed) {
    throw "Tray recovery report did not satisfy the release gate."
}

Write-Host "Tray recovery report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
Write-Host "Stable cycles: $($cycles.Count)/8"
Write-Host (
    "Growth: handles {0}, threads {1}, private memory {2:N1} MB" -f
        [int]$report.growth.handle_count,
        [int]$report.growth.thread_count,
        ([long]$report.growth.private_memory_bytes / 1MB))
