param(
    [string]$OutputDirectory,
    [ValidateRange(3000, 30000)]
    [int]$IdleMilliseconds = 9000,
    [ValidateRange(30, 180)]
    [int]$TimeoutSeconds = 90,
    [string]$WprPath = "$env:SystemRoot\System32\wpr.exe",
    [switch]$NoBuild,
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

$isWindows = [Environment]::OSVersion.Platform -eq "Win32NT"
$isAdministrator = $isWindows -and (Test-IsAdministrator)
$wprAvailable = Test-Path -LiteralPath $WprPath -PathType Leaf
$profileOutput = if ($wprAvailable) {
    @(& $WprPath -profiles 2>&1) -join "`n"
} else {
    ""
}
$requiredProfilesAvailable = (
    $profileOutput -match "(?m)^\s*CPU\s+" `
    -and $profileOutput -match "(?m)^\s*DesktopComposition\s+")
$wpaExporter = Get-Command wpaexporter.exe -ErrorAction SilentlyContinue
$preflight = [ordered]@{
    schema_version = 1
    windows = $isWindows
    administrator = $isAdministrator
    wpr_available = $wprAvailable
    required_profiles_available = $requiredProfilesAvailable
    wpa_exporter_available = $null -ne $wpaExporter
    requested_profiles = @("CPU.Light", "DesktopComposition.Verbose")
    ready = $isWindows -and $isAdministrator -and $wprAvailable `
        -and $requiredProfilesAvailable
}
if ($PreflightOnly) {
    $preflight | ConvertTo-Json -Depth 4
    exit 0
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw "OutputDirectory is required unless PreflightOnly is used."
}
if (-not $isWindows) {
    throw "Native performance capture requires Windows."
}
if (-not $isAdministrator) {
    throw "Native performance capture requires an elevated Administrator PowerShell session."
}
if (-not $wprAvailable) {
    throw "Windows Performance Recorder was not found: $WprPath"
}
if (-not $requiredProfilesAvailable) {
    throw "WPR does not expose the required CPU and DesktopComposition profiles."
}

$trackedStatus = ((& git -C $PSScriptRoot status `
    --porcelain --untracked-files=no) -join "`n")
if (-not [string]::IsNullOrWhiteSpace($trackedStatus)) {
    throw "Native performance evidence requires a clean tracked worktree."
}
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
$outputRoot = Resolve-RepositoryPath $OutputDirectory
if (Test-Path -LiteralPath $outputRoot) {
    throw "Native performance evidence directory already exists: $outputRoot"
}

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "dotnet CLI was not found."
    }
    $dotnet = $dotnetCommand.Source
}
$project = Join-Path $PSScriptRoot (
    "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj")
if (-not $NoBuild) {
    & $dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Native performance Release build failed."
    }
}

$hostRoot = Join-Path $PSScriptRoot (
    "src\LongBetterWindows.Host\bin\Release\net8.0-windows")
$hostExecutable = Join-Path $hostRoot "LongBetterWindows.Host.exe"
$pluginRoot = Join-Path $hostRoot "Plugins"
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "Release host executable was not found: $hostExecutable"
}
$manifestFiles = @(Get-ChildItem -LiteralPath $pluginRoot `
    -Filter manifest.json -File -Recurse)
$pluginIds = @($manifestFiles | ForEach-Object {
    (Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 |
        ConvertFrom-Json).id
} | Sort-Object -Unique)
if ($manifestFiles.Count -ne 25 -or $pluginIds.Count -ne 25) {
    throw "Native performance capture requires 25 distinct built-in plugins."
}

New-Item -ItemType Directory -Path $outputRoot | Out-Null
$runRoot = Join-Path $outputRoot "run"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$performanceReport = Join-Path $outputRoot "plugin-page-performance.json"
$tracePath = Join-Path $outputRoot "cpu-desktop-composition.etl"
$startedRecording = $false
$hostExitCode = $null
$recordedAt = [DateTimeOffset]::UtcNow

try {
    $startOutput = @(& $WprPath `
        -start CPU.Light `
        -start DesktopComposition.Verbose `
        -filemode 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "WPR start failed with exit code ${LASTEXITCODE}: $($startOutput -join "`n")"
    }
    $startedRecording = $true

    $arguments = @(
        "--theme", "dark",
        "--plugins-dir", "`"$pluginRoot`"",
        "--quality-plugin-page-performance-report",
        "`"$performanceReport`"",
        "--quality-idle-ms", $IdleMilliseconds.ToString(),
        "--quality-width", "1120",
        "--quality-height", "760"
    )
    $process = Start-Process -FilePath $hostExecutable `
        -ArgumentList $arguments `
        -WorkingDirectory $runRoot `
        -WindowStyle Hidden `
        -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Native performance host timed out after $TimeoutSeconds seconds."
    }
    $hostExitCode = $process.ExitCode
    if ($hostExitCode -ne 0) {
        throw "Native performance host exited with code $hostExitCode."
    }
    if (-not (Test-Path -LiteralPath $performanceReport -PathType Leaf)) {
        throw "Host produced no plugin-page performance report."
    }

    $stopOutput = @(& $WprPath `
        -stop $tracePath `
        "Long Assistant plugin page idle performance" `
        -compress `
        -skipPdbGen 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "WPR stop failed with exit code ${LASTEXITCODE}: $($stopOutput -join "`n")"
    }
    $startedRecording = $false
}
finally {
    if ($startedRecording) {
        & $WprPath -cancel 2>&1 | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $tracePath -PathType Leaf)) {
    throw "WPR produced no ETL trace."
}
$performance = Get-Content -LiteralPath $performanceReport `
    -Raw -Encoding UTF8 | ConvertFrom-Json
$manifest = [ordered]@{
    schema_version = 1
    captured_at = $recordedAt.ToString("O")
    classification = "native_wpr_cpu_desktop_composition"
    source_commit = $sourceCommit
    source_dirty = $false
    administrator = $true
    profiles = @("CPU.Light", "DesktopComposition.Verbose")
    analysis_status = "pending_analysis"
    release_gate_passed = $false
    host_exit_code = $hostExitCode
    plugin_count = 25
    idle_milliseconds = $IdleMilliseconds
    host_executable_sha256 = (
        Get-FileHash -LiteralPath $hostExecutable -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    trace_file = [IO.Path]::GetFileName($tracePath)
    trace_sha256 = (
        Get-FileHash -LiteralPath $tracePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    trace_size_bytes = (Get-Item -LiteralPath $tracePath).Length
    performance_report_file = [IO.Path]::GetFileName($performanceReport)
    performance_report_sha256 = (
        Get-FileHash -LiteralPath $performanceReport -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    performance_sample_count = @($performance.samples).Count
}
$manifestPath = Join-Path $outputRoot "native-performance-evidence.json"
$manifest | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Native WPR capture completed; WPA analysis is still pending."
Write-Host "Evidence: $manifestPath"
