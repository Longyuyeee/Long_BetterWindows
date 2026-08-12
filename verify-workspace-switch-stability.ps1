param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath = "",
    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 180
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
    throw "Workspace switch evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/workspace-switch-$stamp/workspace-switch.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
if (Test-Path -LiteralPath $outputFile) {
    throw "Workspace switch output already exists: $outputFile"
}
New-Item -ItemType Directory -Force `
    -Path ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @(
        "--quality-workspace-switch-report", $outputFile,
        "--theme", "dark",
        "--language", "zh-CN") `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        & taskkill.exe /PID $process.Id /T /F 2>&1 | Out-Null
        throw "Workspace switch probe timed out after $TimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) {
        throw "Workspace switch probe failed with exit code $($process.ExitCode)."
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
    throw "Workspace switch report was not generated: $outputFile"
}
$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$cycles = @($report.cycles)
$samples = @($report.samples)
$plugins = @($report.plugin_ids)
$managementPages = @($report.management_pages)
$invalidCycles = @($cycles | Where-Object {
    -not [bool]$_.ManagementPassed -or
    -not [bool]$_.PluginIdentityPassed -or
    -not [bool]$_.ModuleCountsPassed
})
$passed =
    [bool]$report.passed -and
    [bool]$report.switching_passed -and
    [bool]$report.growth_passed -and
    [bool]$report.cleanup_passed -and
    [int]$report.cycle_count -eq 12 -and
    $plugins.Count -eq 3 -and
    $managementPages.Count -eq 4 -and
    [int]$report.expected_module_count -eq 7 -and
    [int]$report.expected_plugin_runtime_module_count -eq 3 -and
    $cycles.Count -eq 12 -and
    $samples.Count -eq 13 -and
    $invalidCycles.Count -eq 0
if (-not $passed) {
    throw "Workspace switch report did not satisfy the release gate."
}

Write-Host "Workspace switch report: $outputFile"
Write-Host "Source commit: $($sourceCommit.ToLowerInvariant())"
Write-Host "Stable cycles: $($cycles.Count)/12"
Write-Host "Plugins/pages: $($plugins.Count)/$($managementPages.Count)"
Write-Host (
    "Growth: handles {0}, threads {1}, private memory {2:N1} MB" -f
        [int]$report.growth.handle_count,
        [int]$report.growth.thread_count,
        ([long]$report.growth.private_memory_bytes / 1MB))
