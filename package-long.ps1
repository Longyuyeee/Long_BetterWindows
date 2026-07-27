param(
    [string]$Version,
    [ValidateSet("All", "FrameworkDependent", "SelfContained")]
    [string]$PackageKind = "All",
    [switch]$ReplaceExisting,
    [switch]$AllowDirty,
    [switch]$OpenOutput,
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = $PSScriptRoot
$releaseScript = Join-Path $repositoryRoot "release.ps1"
$project = Join-Path $repositoryRoot `
    "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj"
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Long Assistant project file was not found: $project"
}
$projectText = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$projectVersion = [regex]::Match(
    $projectText,
    "<Version>([^<]+)</Version>").Groups[1].Value
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw "The project Version property could not be read."
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}
if ($Version -ne $projectVersion) {
    throw "Requested version $Version does not match project version $projectVersion."
}

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        $dotnet = $dotnetCommand.Source
    }
}
$dotnetAvailable = Test-Path -LiteralPath $dotnet -PathType Leaf
$releaseScriptAvailable = Test-Path -LiteralPath `
    $releaseScript -PathType Leaf
$releaseRoot = Join-Path $repositoryRoot `
    "artifacts\releases\v$Version"
$trackedStatus = @(& git -C $repositoryRoot status `
    --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to inspect the tracked worktree."
}
$sourceDirty = $trackedStatus.Count -gt 0

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$isccAvailable = @($isccCandidates | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
}).Count -gt 0 -or $null -ne (
    Get-Command ISCC.exe -ErrorAction SilentlyContinue)
$installerRequired = $PackageKind -ne "FrameworkDependent"

if ($PreflightOnly) {
    [ordered]@{
        schema_version = 1
        classification = "long_assistant_package_preflight"
        version = $Version
        package_kind = $PackageKind
        repository_root = $repositoryRoot
        output_directory = $releaseRoot
        output_exists = Test-Path -LiteralPath $releaseRoot
        source_dirty = $sourceDirty
        allow_dirty = [bool]$AllowDirty
        dotnet_available = $dotnetAvailable
        release_script_available = $releaseScriptAvailable
        installer_required = $installerRequired
        inno_setup_available = $isccAvailable
        ready = $dotnetAvailable -and $releaseScriptAvailable `
            -and (-not $installerRequired -or $isccAvailable) `
            -and (-not $sourceDirty -or $AllowDirty)
    } | ConvertTo-Json -Depth 4
    exit 0
}

if (-not $dotnetAvailable) {
    throw "dotnet CLI was not found. Install the .NET 8 SDK."
}
if (-not $releaseScriptAvailable) {
    throw "Release pipeline was not found: $releaseScript"
}
if ($installerRequired -and -not $isccAvailable) {
    throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup"
}
if ($sourceDirty -and -not $AllowDirty) {
    throw "Tracked worktree is dirty. Commit changes first, or use -AllowDirty only for a local test package."
}

$force = [bool]$ReplaceExisting
if ((Test-Path -LiteralPath $releaseRoot) -and -not $force) {
    if (-not [Environment]::UserInteractive) {
        throw "Output already exists: $releaseRoot. Use -ReplaceExisting."
    }
    $answer = Read-Host `
        "Output already exists. Replace artifacts/releases/v$Version? [y/N]"
    $force = $answer -match "^(?i:y|yes)$"
    if (-not $force) {
        throw "Packaging was cancelled; existing output was preserved."
    }
}

$releaseParameters = @{
    Version = $Version
    PackageKind = $PackageKind
}
if ($force) {
    $releaseParameters.Force = $true
}

Push-Location $repositoryRoot
try {
    & $releaseScript @releaseParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Release pipeline failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$manifestPath = Join-Path $releaseRoot "release-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Packaging completed without a release manifest."
}
$manifest = Get-Content -LiteralPath $manifestPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$manifest.version -ne $Version) {
    throw "Release manifest version does not match the requested version."
}
if ($installerRequired -and @($manifest.installers).Count -ne 1) {
    throw "Packaging completed without exactly one Setup.exe installer."
}

Write-Host ""
Write-Host "Long Assistant packages are ready:"
Write-Host $releaseRoot
@($manifest.packages) | ForEach-Object {
    Write-Host "  ZIP   $($_.file)"
}
@($manifest.installers) | ForEach-Object {
    Write-Host "  SETUP $($_.file)"
}
Write-Host "  HASH  SHA256SUMS.txt"
Write-Host ""
Write-Host "This is a candidate build. Manual release gates still apply."

if ($OpenOutput) {
    Start-Process explorer.exe -ArgumentList @($releaseRoot)
}
