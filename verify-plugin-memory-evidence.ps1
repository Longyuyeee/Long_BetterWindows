param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [string]$ExpectedCommit,
    [ValidateRange(3, 20)]
    [int]$MinimumSamples = 5,
    [ValidateRange(1000, 15000)]
    [int]$MinimumIdleMilliseconds = 9000
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

$root = Resolve-RepositoryPath $EvidenceDirectory
$reportPath = Join-Path $root "plugin-memory-report.json"
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "Plugin memory report was not found: $reportPath"
}
$report = Get-Content -LiteralPath $reportPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
}

$errors = [Collections.Generic.List[string]]::new()
if ($report.source_commit -ne $ExpectedCommit) {
    $errors.Add("Source commit does not match the expected commit.")
}
if ($report.source_dirty -ne $false) {
    $errors.Add("Memory evidence was not produced from a clean tracked worktree.")
}
if ($report.configuration -ne "Release") {
    $errors.Add("Memory evidence was not produced from a Release build.")
}
if ([int]$report.plugin_count -ne 25 `
    -or @($report.unique_plugin_ids).Count -ne 25) {
    $errors.Add("Memory evidence did not bind 25 distinct plugins.")
}
if ([int]$report.sample_count -lt $MinimumSamples `
    -or @($report.samples).Count -lt $MinimumSamples) {
    $errors.Add("Memory evidence has too few samples.")
}
if ([int]$report.idle_milliseconds -lt $MinimumIdleMilliseconds) {
    $errors.Add("Memory evidence idle interval is too short.")
}
if ([double]$report.maximum_working_set_mb `
    -ge [double]$report.working_set_limit_mb) {
    $errors.Add("Maximum working set did not remain strictly below the limit.")
}
if ($report.passed -ne $true) {
    $errors.Add("Memory probe did not pass.")
}
if (@($report.samples | Where-Object {
        [int]$_.plugins -ne 25 `
        -or [int]$_.commands -lt [int]$report.command_count_minimum `
        -or [double]$_.working_set_mb `
            -ge [double]$report.working_set_limit_mb
    }).Count -gt 0) {
    $errors.Add("One or more memory samples violated the release contract.")
}
if ([string]::IsNullOrWhiteSpace(
        [string]$report.host_executable_sha256) `
    -or [string]$report.host_executable_sha256 -notmatch "^[0-9a-f]{64}$") {
    $errors.Add("Host executable SHA-256 is missing or invalid.")
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host "Plugin memory evidence passed the strict release contract."
Write-Host (
    "Samples: {0}; median: {1} MB; maximum: {2} MB; limit: <{3} MB" -f
    @($report.samples).Count,
    $report.median_working_set_mb,
    $report.maximum_working_set_mb,
    $report.working_set_limit_mb)
