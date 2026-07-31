#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Build, publisher-sign and locally verify the LPWP reference Widget bundle.
.DESCRIPTION
  Requires an existing RSA publisher private key. The key is never copied to output,
  and a key located inside this repository must be covered by .gitignore.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [Parameter(Mandatory=$true)] [string] $PrivateKeyPath,
    [Parameter(Mandatory=$true)] [string] $PublisherKeyId,
    [Parameter(Mandatory=$true)] [string] $PublisherName,
    [Parameter(Mandatory=$true)] [uri] $BasePackageUri
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$privateKey = [IO.Path]::GetFullPath($PrivateKeyPath)
if (Test-Path -LiteralPath $outputRoot) { throw "Output directory already exists: $outputRoot" }
if (-not (Test-Path -LiteralPath $privateKey -PathType Leaf)) { throw 'Publisher private key was not found.' }
if ($BasePackageUri.Scheme -ne 'https' -or -not $BasePackageUri.AbsolutePath.EndsWith('/')) {
    throw 'BasePackageUri must be an absolute HTTPS URI ending with a slash.'
}
$trackedChanges = @(& git -C $repoRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $trackedChanges.Count -ne 0) {
    throw 'Signed reference publishing requires a clean tracked source commit.'
}

function Test-IsWithin([string] $Directory, [string] $Candidate) {
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath($Candidate)
    return $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
}

if (Test-IsWithin $repoRoot $privateKey) {
    & git -C $repoRoot check-ignore -q -- $privateKey
    if ($LASTEXITCODE -ne 0) {
        throw 'A publisher private key inside the repository must be covered by .gitignore.'
    }
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("long-reference-sign-" + [Guid]::NewGuid().ToString('N'))
$packagesRoot = Join-Path $workRoot 'packages'
$sourcePath = Join-Path $workRoot 'marketplace-source.json'
try {
    [IO.Directory]::CreateDirectory($workRoot) | Out-Null
    & (Join-Path $repoRoot 'build-reference-widget.ps1') -OutputDir $packagesRoot
    if ($LASTEXITCODE -ne 0) { throw 'Reference Widget package build failed.' }

    $manifestPath = Join-Path $repoRoot 'samples\LongWidgetReference\manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $package = Get-ChildItem -LiteralPath $packagesRoot -Filter '*.lpak' | Select-Object -First 1
    if ($null -eq $package) { throw 'Reference Widget package was not produced.' }
    $source = [ordered]@{
        SchemaVersion = 1
        Entries = @([ordered]@{
            Id = [string]$manifest.id
            Name = [string]$manifest.name
            Summary = 'Official LPWP 1.0 reference Widget package.'
            Description = 'Reference implementation for Widget lifecycle, state and host capability negotiation.'
            Category = 'Developer Tools'
            Tags = @('lpwp', 'widget', 'reference')
            Versions = @([ordered]@{
                Version = [string]$manifest.version
                PackageFile = $package.Name
                PublishedAt = [DateTimeOffset]::UtcNow.ToString('O')
                ReleaseNotes = 'LPWP 1.0 signed reference package.'
            })
        })
    }
    [IO.File]::WriteAllText(
        $sourcePath, ($source | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

    $publishArguments = @{
        SourceCatalog = $sourcePath
        PackagesDir = $packagesRoot
        OutputDir = $outputRoot
        PrivateKeyPath = $privateKey
        PublisherKeyId = $PublisherKeyId
        PublisherName = $PublisherName
        BasePackageUri = $BasePackageUri
    }
    & (Join-Path $repoRoot 'publish-marketplace.ps1') @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Reference Widget publisher signing failed.' }

    $publishReport = Get-Content -LiteralPath (Join-Path $outputRoot 'publish-report.json') `
        -Raw -Encoding UTF8 | ConvertFrom-Json
    $verificationPath = Join-Path $outputRoot 'bundle-verification-report.json'
    & (Join-Path $repoRoot 'verify-marketplace-bundle.ps1') `
        -BundleDirectory $outputRoot -ExpectedPublisherKeyId $PublisherKeyId `
        -ExpectedPublicKeyFingerprint ([string]$publishReport.PublicKeyFingerprint) `
        -ReportPath $verificationPath
    if ($LASTEXITCODE -ne 0) { throw 'Signed reference Widget verification failed.' }

    $verification = Get-Content -LiteralPath $verificationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $summary = [ordered]@{
        schema_version = 1
        generated_utc = [DateTimeOffset]::UtcNow.ToString('O')
        source_commit = (& git -C $repoRoot rev-parse HEAD).Trim()
        plugin_id = [string]$manifest.id
        version = [string]$manifest.version
        publisher = [string]$verification.Publisher
        publisher_key_id = [string]$verification.PublisherKeyId
        public_key_fingerprint = [string]$verification.PublicKeyFingerprint
        package_sha256 = [string]$verification.Packages[0].Sha256
        signature_verified = $true
        external_long_grid_e2e = $false
        manual_widget_validation = $false
        release_eligible = $false
    }
    [IO.File]::WriteAllText(
        (Join-Path $outputRoot 'signed-reference-widget-report.json'),
        ($summary | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Write-Host "Signed reference Widget verified: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
