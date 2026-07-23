#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Capture signed Sparse Package and real Windows 11 Explorer interaction evidence.
.DESCRIPTION
  Run only in a new Windows user or disposable VM. The script registers the signed
  package for the current user, opens an isolated folder, waits for three operator
  screenshots, and always attempts to unregister the package in a finally block.
#>
param(
    [Parameter(Mandatory=$true)] [string] $SignedPackage,
    [Parameter(Mandatory=$true)] [string] $SignatureReport,
    [Parameter(Mandatory=$true)] [string] $BuildReport,
    [Parameter(Mandatory=$true)] [string] $ExternalLocation,
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)] [string] $ExpectedCertificateThumbprint,
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [string] $ScreenshotInputDirectory,
    [Parameter(Mandatory=$true)] [string] $EnvironmentLabel,
    [Parameter(Mandatory=$true)] [switch] $ConfirmCleanUserEnvironment,
    [Parameter(Mandatory=$true)] [switch] $RequireTimestamp,
    [switch] $PreflightOnly
)

$ErrorActionPreference = 'Stop'
$identityName = 'Long.LongBetterWindows'
$completionPhrase = 'SPARSE EXPLORER CHECKS COMPLETE'
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
$expectedThumbprint = $ExpectedCertificateThumbprint.Replace(' ', '').ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'ExpectedSourceCommit must be a full 40-character Git commit SHA.'
}
if ($expectedThumbprint -notmatch '^[0-9a-f]{40,128}$') {
    throw 'ExpectedCertificateThumbprint is invalid.'
}
if (-not $ConfirmCleanUserEnvironment) {
    throw 'ConfirmCleanUserEnvironment is required for a new Windows user or disposable VM.'
}
if (-not $RequireTimestamp) {
    throw 'RequireTimestamp must be explicitly supplied for formal Explorer evidence.'
}
if ([string]::IsNullOrWhiteSpace($EnvironmentLabel)) {
    throw 'EnvironmentLabel must not be empty.'
}
if (-not [Environment]::UserInteractive) {
    throw 'An interactive Windows desktop session is required.'
}
if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'A 64-bit Windows environment is required.'
}

$os = Get-CimInstance Win32_OperatingSystem
if ([int]$os.BuildNumber -lt 22000) {
    throw 'Windows 11 build 22000 or newer is required.'
}
if ($null -eq (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    throw 'Windows Explorer is not running.'
}
if (Get-Process -Name 'LongBetterWindows.Host' -ErrorAction SilentlyContinue) {
    throw 'LongBetterWindows.Host must be closed before capture.'
}
if (Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue) {
    throw 'Long sparse package is already registered. Start from a clean current-user state.'
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagePath = [IO.Path]::GetFullPath($SignedPackage)
$signatureReportPath = [IO.Path]::GetFullPath($SignatureReport)
$buildReportPath = [IO.Path]::GetFullPath($BuildReport)
$externalRoot = [IO.Path]::GetFullPath($ExternalLocation)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$screenshotInputRoot = if ([string]::IsNullOrWhiteSpace($ScreenshotInputDirectory)) {
    $null
}
else {
    [IO.Path]::GetFullPath($ScreenshotInputDirectory)
}
foreach ($path in @($packagePath, $signatureReportPath, $buildReportPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required candidate evidence file was not found: $path"
    }
}
if (Test-Path -LiteralPath $outputRoot) {
    throw "Sparse Explorer evidence output already exists: $outputRoot"
}
$screenshotNames = @(
    'selection-primary-menu.png',
    'background-primary-menu.png',
    'note-invocation.png'
)
if (-not $PreflightOnly) {
    if ($null -eq $screenshotInputRoot -or
        -not (Test-Path -LiteralPath $screenshotInputRoot -PathType Container)) {
        throw 'ScreenshotInputDirectory is required for interactive capture.'
    }
    foreach ($name in $screenshotNames) {
        if (Test-Path -LiteralPath (Join-Path $screenshotInputRoot $name)) {
            throw "Screenshot input must not predate this capture session: $name"
        }
    }
}

$build = Get-Content -LiteralPath $buildReportPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$signing = Get-Content -LiteralPath $signatureReportPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($build.classification -ne 'unsigned_sparse_package_build' -or
    -not [bool]$build.passed -or
    -not [bool]$build.tracked_source_clean) {
    throw 'Sparse package build report is not a passing clean-source build.'
}
if ([string]$build.source_commit -ne $expectedCommit) {
    throw 'Sparse package build report source commit does not match ExpectedSourceCommit.'
}
if ([string]$build.architecture -ne 'x64' -or
    [string]$build.shell_extension.machine_verified -ne 'x64' -or
    -not [bool]$build.shell_extension.required_com_exports_verified) {
    throw 'Sparse package build report does not prove the x64 COM binary.'
}
if ($signing.classification -ne 'signed_sparse_package' -or
    -not [bool]$signing.signature_valid) {
    throw 'Sparse package signature report is not passing.'
}
if ([string]$signing.certificate_thumbprint -ne $expectedThumbprint) {
    throw 'Signature report certificate thumbprint does not match the expected certificate.'
}
if ([string]::IsNullOrWhiteSpace([string]$signing.timestamp_url)) {
    throw 'Formal signed package evidence requires an HTTPS timestamp.'
}
if (-not ([Uri][string]$signing.timestamp_url).Scheme.Equals(
    'https',
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Signature report timestamp URL is not HTTPS.'
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageHash -ne [string]$signing.output_sha256 -or
    [string]$signing.input_sha256 -ne [string]$build.package.sha256) {
    throw 'Signed package, signing report, and unsigned build report hashes do not form one chain.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $packagePath
if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
    $null -eq $signature.SignerCertificate) {
    throw 'Signed package does not have a valid trusted Authenticode signature.'
}
if ($signature.SignerCertificate.Thumbprint.ToLowerInvariant() -ne $expectedThumbprint) {
    throw 'Signed package certificate thumbprint does not match the expected certificate.'
}
if ($null -eq $signature.TimeStamperCertificate) {
    throw 'Signed package does not contain a verifiable timestamp countersignature.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $manifestEntry) { throw 'Signed package manifest is missing.' }
    $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally {
    $archive.Dispose()
}
$packageIdentity = $manifest.Package.Identity
if ($packageIdentity.Name -ne $identityName -or
    $packageIdentity.ProcessorArchitecture -ne 'x64') {
    throw 'Signed package identity or architecture is invalid.'
}
if (-not [string]::Equals(
    $signature.SignerCertificate.Subject,
    [string]$packageIdentity.Publisher,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Signed package certificate Subject does not match manifest Publisher.'
}

$hostPath = Join-Path $externalRoot 'LongBetterWindows.Host.exe'
$shellPath = Join-Path $externalRoot `
    'ShellExtension\LongBetterWindows.ShellExtension.dll'
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $shellPath -PathType Leaf)) {
    throw 'External location is missing the host EXE or shell extension DLL.'
}
$shellHash = (Get-FileHash -LiteralPath $shellPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($shellHash -ne [string]$build.shell_extension.sha256) {
    throw 'External shell extension hash does not match the clean build report.'
}
$hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hostHash -ne [string]$build.host_sha256) {
    throw 'External host hash does not match the clean build report.'
}

if ($PreflightOnly) {
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    $preflightPath = Join-Path $outputRoot `
        'sparse-package-explorer-preflight.json'
    [ordered]@{
        schema_version = 1
        generated_at = [DateTimeOffset]::UtcNow.ToString('O')
        classification = 'sparse_package_explorer_preflight'
        preflight_only = $true
        source_commit = $expectedCommit
        package_sha256 = $packageHash
        certificate_thumbprint = $expectedThumbprint
        timestamp_thumbprint = $signature.TimeStamperCertificate.Thumbprint.ToLowerInvariant()
        external_location = $externalRoot
        host_sha256 = $hostHash
        shell_extension_sha256 = $shellHash
        package_registration_attempted = $false
        explorer_interaction_attempted = $false
        passed = $true
    } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $preflightPath -Encoding UTF8
    Write-Output 'Sparse Package Explorer read-only preflight passed.'
    Write-Output 'This result cannot replace interactive capture and independent approval.'
    Write-Output "Preflight: $preflightPath"
    exit 0
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$capturedPackage = Join-Path $outputRoot ([IO.Path]::GetFileName($packagePath))
$capturedBuildReport = Join-Path $outputRoot 'sparse-package-build.json'
$capturedSignatureReport = Join-Path $outputRoot 'sparse-package-signature.json'
Copy-Item -LiteralPath $packagePath -Destination $capturedPackage
Copy-Item -LiteralPath $buildReportPath -Destination $capturedBuildReport
Copy-Item -LiteralPath $signatureReportPath -Destination $capturedSignatureReport
$targetRoot = Join-Path $outputRoot 'explorer-target'
[IO.Directory]::CreateDirectory($targetRoot) | Out-Null
$legacySelectionBefore = Test-Path -LiteralPath `
    'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\shell\LongNote'
$legacyBackgroundBefore = Test-Path -LiteralPath `
    'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LongNote'
$registered = $false
$cleanupSucceeded = $false
$registrationState = $null
$failure = $null
try {
    $registrationOutput = & (Join-Path $repoRoot 'manage-sparse-package.ps1') `
        -Action Register -PackagePath $packagePath -ExternalLocation $externalRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Sparse package registration failed: $($registrationOutput -join ' ')"
    }
    $registrationState = $registrationOutput |
        Select-Object -Last 1 |
        ConvertFrom-Json
    if (-not [bool]$registrationState.succeeded -or
        -not [bool]$registrationState.installed -or
        [string]$registrationState.version -ne [string]$packageIdentity.Version -or
        [string]$registrationState.external_location -ne $externalRoot) {
        throw 'Registered sparse package state does not match the candidate and external location.'
    }
    $registered = $true

    Start-Process -FilePath explorer.exe -ArgumentList @($targetRoot) | Out-Null
    Write-Output ''
    Write-Output 'Perform all three checks in the opened Explorer folder:'
    Write-Output '1. Right-click the folder selection and capture the primary menu.'
    Write-Output '2. Right-click the directory background and capture the primary menu.'
    Write-Output '3. Invoke "备注此文件夹", confirm the exact target, close Long, and capture the note UI.'
    Write-Output "Save the PNG files in: $screenshotInputRoot"
    $screenshotNames | ForEach-Object { Write-Output "  $_" }
    $confirmation = Read-Host "After closing Long, type exactly: $completionPhrase"
    if ($confirmation -cne $completionPhrase) {
        throw 'Interactive completion phrase did not match.'
    }
    if (Get-Process -Name 'LongBetterWindows.Host' -ErrorAction SilentlyContinue) {
        throw 'LongBetterWindows.Host is still running after the interaction checks.'
    }

    Add-Type -AssemblyName System.Drawing
    foreach ($name in $screenshotNames) {
        $source = Join-Path $screenshotInputRoot $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required Explorer screenshot is missing: $name"
        }
        if ((Get-Item -LiteralPath $source).Length -lt 10KB) {
            throw "Explorer screenshot is unexpectedly small: $name"
        }
        $image = [Drawing.Image]::FromFile($source)
        try {
            if ($image.Width -lt 640 -or $image.Height -lt 360) {
                throw "Explorer screenshot dimensions are too small: $name"
            }
        }
        finally {
            $image.Dispose()
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot $name)
    }
    if ($null -eq (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
        throw 'Windows Explorer is no longer running after the interaction checks.'
    }
}
catch {
    $failure = $_
}
finally {
    if ($registered -or
        (Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue)) {
        $cleanupOutput = & (Join-Path $repoRoot 'manage-sparse-package.ps1') `
            -Action Unregister
        $cleanupSucceeded = $LASTEXITCODE -eq 0 -and
            $null -eq (Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue)
    }
    else {
        $cleanupSucceeded = $true
    }
}
if (-not $cleanupSucceeded) {
    throw 'Sparse package cleanup failed; manual removal is required before this machine can be reused.'
}
if ($null -ne $failure) {
    throw $failure
}

$legacySelectionAfter = Test-Path -LiteralPath `
    'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\shell\LongNote'
$legacyBackgroundAfter = Test-Path -LiteralPath `
    'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\LongNote'
if ($legacySelectionAfter -ne $legacySelectionBefore -or
    $legacyBackgroundAfter -ne $legacyBackgroundBefore) {
    throw 'Compatible legacy context-menu state changed during Sparse Package acceptance.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$files = @(
    [IO.Path]::GetFileName($capturedPackage),
    'sparse-package-build.json',
    'sparse-package-signature.json'
) + $screenshotNames
$fileEvidence = @($files | ForEach-Object {
    $path = Join-Path $outputRoot $_
    [ordered]@{
        file = $_
        bytes = (Get-Item -LiteralPath $path).Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$evidence = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'sparse_package_explorer_evidence'
    environment = [ordered]@{
        label = $EnvironmentLabel.Trim()
        machine = [Environment]::MachineName
        user = [Environment]::UserName
        user_sid = $identity.User.Value
        os_caption = [string]$os.Caption
        os_version = [string]$os.Version
        os_build = [string]$os.BuildNumber
        interactive = $true
        operator_asserted_clean_user = $true
    }
    candidate = [ordered]@{
        source_commit = $expectedCommit
        identity_name = $identityName
        version = [string]$packageIdentity.Version
        publisher = [string]$packageIdentity.Publisher
        certificate_thumbprint = $expectedThumbprint
        timestamp_thumbprint = $signature.TimeStamperCertificate.Thumbprint.ToLowerInvariant()
        external_location = $externalRoot
        host_sha256 = $hostHash
        shell_extension_sha256 = $shellHash
    }
    automated_checks = [ordered]@{
        passed = $true
        signed_package_valid = $true
        clean_build_chain_valid = $true
        registration_version_matched = $true
        external_location_matched = $true
        explorer_running_after_checks = $true
        package_removed_after_capture = $true
        legacy_menu_state_unchanged = $true
    }
    files = $fileEvidence
    operator_capture = [ordered]@{
        completion_phrase_confirmed = $true
        screenshots = $screenshotNames
    }
    human_review = [ordered]@{
        status = 'pending'
        reviewer = $null
        reviewed_at = $null
        notes = $null
        checklist = [ordered]@{
            selection_primary_menu = $false
            background_primary_menu = $false
            correct_note_target = $false
            explorer_stable = $false
            uninstall_removed_menu = $false
        }
    }
}
$evidencePath = Join-Path $outputRoot 'sparse-package-explorer-evidence.json'
$evidence | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $evidencePath -Encoding UTF8
Write-Output 'Sparse Package Explorer capture passed; independent review remains pending.'
Write-Output "Evidence: $evidencePath"
