param(
    [string]$HostDirectory =
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath,
    [int]$ExpectedPluginCount = 25,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

$hostRoot = Resolve-RepositoryPath $HostDirectory
$hostExecutable = Join-Path $hostRoot "LongBetterWindows.Host.exe"
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "Host executable was not found: $hostExecutable"
}
if ($ExpectedPluginCount -le 0) {
    throw "ExpectedPluginCount must be positive."
}
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Unable to resolve the source commit."
}
$trackedStatus = @(& git -C $PSScriptRoot status `
    --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the tracked source tree."
}
if ($trackedStatus.Count -gt 0) {
    throw "Taskbar identity evidence requires a clean tracked source tree."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath =
        "artifacts/quality/taskbar-identity-$stamp/taskbar-identity.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputFile)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$process = Start-Process `
    -FilePath $hostExecutable `
    -ArgumentList @("--quality-taskbar-identity-report", $outputFile) `
    -WorkingDirectory $hostRoot `
    -WindowStyle Hidden `
    -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force
    throw "Taskbar identity probe timed out after $TimeoutSeconds seconds."
}
if ($process.ExitCode -ne 0) {
    throw "Taskbar identity probe failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf)) {
    throw "Taskbar identity report was not generated: $outputFile"
}

$report = Get-Content -LiteralPath $outputFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$report | Add-Member -NotePropertyName source_commit `
    -NotePropertyValue $sourceCommit.ToLowerInvariant() -Force
$report | Add-Member -NotePropertyName source_dirty `
    -NotePropertyValue $false -Force
$report | Add-Member -NotePropertyName host_executable_sha256 `
    -NotePropertyValue (
        (Get-FileHash -LiteralPath $hostExecutable -Algorithm SHA256).
            Hash.ToLowerInvariant()) -Force
$report | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $outputFile -Encoding UTF8
$windows = @($report.windows)
$actualIdentities = @($windows |
    ForEach-Object { [string]$_.actual_app_user_model_id } |
    Sort-Object -Unique)
$icons = @($windows |
    ForEach-Object { [string]$_.icon_sha256 } |
    Sort-Object -Unique)
$invalidWindows = @($windows | Where-Object {
    [string]$_.expected_app_user_model_id -ne
        [string]$_.actual_app_user_model_id -or
    [string]::IsNullOrWhiteSpace([string]$_.icon_sha256) -or
    [bool]$_.has_owner -or
    -not [bool]$_.show_in_taskbar
})
$passed =
    [bool]$report.passed -and
    [int]$report.expected_plugin_count -eq $ExpectedPluginCount -and
    [int]$report.plugin_count -eq $ExpectedPluginCount -and
    $windows.Count -eq $ExpectedPluginCount -and
    $actualIdentities.Count -eq $ExpectedPluginCount -and
    $icons.Count -eq $ExpectedPluginCount -and
    $invalidWindows.Count -eq 0

Write-Host (
    "Taskbar identity matrix: {0}/{1} plugin windows, {2} identities, {3} icons" `
        -f $windows.Count,
            $ExpectedPluginCount,
            $actualIdentities.Count,
            $icons.Count)
Write-Host "Evidence: $outputFile"
if (-not $passed) {
    throw "Taskbar identity matrix did not satisfy the release contract."
}
