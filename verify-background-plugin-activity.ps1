param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [ValidateRange(30, 180)]
    [int]$TimeoutSeconds = 90
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
    throw "Background activity evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/background-activity-$stamp/background-activity.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
if (Test-Path -LiteralPath $outputFile) {
    throw "Background activity output already exists: $outputFile"
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-background-activity-report", $outputFile,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null
        throw "Background activity probe timed out after $TimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) {
        throw "Background activity probe failed with exit code $($process.ExitCode)."
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
    throw "Background activity report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$plugins = @($report.plugins)
$expectedIds = @(
    "com.long.clipboardhistory",
    "com.long.hardwaremonitor")
$invalid = @($plugins | Where-Object {
    -not [bool]$_.passed -or
    -not [bool]$_.activity_passed -or
    -not [bool]$_.functional_passed -or
    -not [bool]$_.cleanup_passed -or
    -not [bool]$_.hidden_host_state -or
    -not [bool]$_.restored_host_state -or
    [int]$_.hidden_window_messages -gt 20 -or
    [int]$_.hidden_performance_calls -ne 0 -or
    [int]$_.hidden_clipboard_reads -ne 0 -or
    [double]$_.hidden.cpu_core_percent -gt 5
})
$actualIds = @($plugins | ForEach-Object { [string]$_.plugin_id } |
    Sort-Object)
$passed =
    [bool]$report.passed -and
    [int]$report.visible_ms -eq 6000 -and
    [int]$report.hidden_ms -eq 6000 -and
    [int]$report.restored_ms -eq 4000 -and
    $plugins.Count -eq 2 -and
    $invalid.Count -eq 0 -and
    (Compare-Object ($expectedIds | Sort-Object) $actualIds).Count -eq 0
if (-not $passed) {
    throw "Background activity report did not satisfy the quality gate."
}

Write-Host "Background activity report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
foreach ($plugin in $plugins) {
    Write-Host ("{0}: hidden CPU {1:N2}%, API {2}, messages {3}" -f
        $plugin.plugin_id,
        [double]$plugin.hidden.cpu_core_percent,
        ([int]$plugin.hidden_performance_calls +
            [int]$plugin.hidden_clipboard_reads),
        [int]$plugin.hidden_window_messages)
}
