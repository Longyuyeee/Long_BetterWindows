param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [string]$ExpectedCommit
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
$manifestPath = Join-Path $root "native-performance-evidence.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Native performance manifest was not found: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
}

$errors = [Collections.Generic.List[string]]::new()
if ($manifest.source_commit -ne $ExpectedCommit) {
    $errors.Add("Source commit does not match the expected commit.")
}
if ($manifest.source_dirty -ne $false) {
    $errors.Add("Capture was not produced from a clean tracked worktree.")
}
if ($manifest.administrator -ne $true) {
    $errors.Add("Capture was not produced by an elevated Administrator session.")
}
$profiles = @($manifest.profiles)
if ($profiles.Count -ne 2 `
    -or $profiles -notcontains "CPU.Light" `
    -or $profiles -notcontains "DesktopComposition.Verbose") {
    $errors.Add("Required CPU and DesktopComposition profiles are missing.")
}
if ($manifest.analysis_status -ne "pending_analysis") {
    $errors.Add("Unapproved evidence must remain pending_analysis.")
}
if ($manifest.release_gate_passed -ne $false) {
    $errors.Add("Raw WPR capture cannot mark the release gate passed.")
}
if ([int]$manifest.plugin_count -ne 25) {
    $errors.Add("Capture did not bind all 25 built-in plugins.")
}
if ([long]$manifest.trace_size_bytes -le 0) {
    $errors.Add("ETL trace size is invalid.")
}

foreach ($file in @(
    [PSCustomObject]@{
        Name = [string]$manifest.trace_file
        Hash = [string]$manifest.trace_sha256
    },
    [PSCustomObject]@{
        Name = [string]$manifest.performance_report_file
        Hash = [string]$manifest.performance_report_sha256
    }
)) {
    $path = Join-Path $root $file.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Evidence file is missing: $($file.Name)")
        continue
    }
    $actualHash = (
        Get-FileHash -LiteralPath $path -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($actualHash -ne $file.Hash) {
        $errors.Add("Evidence hash mismatch: $($file.Name)")
    }
}

$performancePath = Join-Path $root (
    [string]$manifest.performance_report_file)
if (Test-Path -LiteralPath $performancePath -PathType Leaf) {
    $performance = Get-Content -LiteralPath $performancePath `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([int]$performance.loaded_plugin_count -ne 25) {
        $errors.Add("Performance report did not load all 25 plugins.")
    }
    if (@($performance.samples).stage -notcontains "plugin_page_idle") {
        $errors.Add("Performance report has no final plugin_page_idle sample.")
    }
    if (@($performance.samples).Count `
        -ne [int]$manifest.performance_sample_count) {
        $errors.Add("Performance sample count does not match the manifest.")
    }
}

if ($errors.Count -gt 0) {
    throw ($errors -join [Environment]::NewLine)
}
Write-Host "Native performance capture is internally consistent."
Write-Host "Analysis status: pending_analysis; release gate remains blocked."
