#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [string] $ExternalLocation,
    [string] $Publisher = 'CN=Long-Development',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string] $Version = '1.9.0.0',
    [switch] $NoHostBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Sparse package output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$externalRoot = if ([string]::IsNullOrWhiteSpace($ExternalLocation)) {
    Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows'
}
else {
    [IO.Path]::GetFullPath($ExternalLocation)
}
if ($NoHostBuild -and $Publisher -ne 'CN=Long-Development') {
    throw 'A custom Publisher requires rebuilding the host identity manifest.'
}

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "The x64 .NET SDK was not found: $dotnet"
}
if (-not $NoHostBuild) {
    $hostManifestSource = Join-Path $repoRoot `
        'src\LongBetterWindows.Host\app.manifest'
    [xml]$hostManifest = Get-Content `
        -LiteralPath $hostManifestSource -Raw -Encoding UTF8
    $hostNamespace = [Xml.XmlNamespaceManager]::new($hostManifest.NameTable)
    $hostNamespace.AddNamespace('msix', 'urn:schemas-microsoft-com:msix.v1')
    $msixIdentity = $hostManifest.SelectSingleNode(
        '/*[local-name()="assembly"]/msix:msix',
        $hostNamespace)
    if ($null -eq $msixIdentity) {
        throw 'Host MSIX identity metadata is missing.'
    }
    $msixIdentity.SetAttribute('publisher', $Publisher)
    $generatedHostManifest = Join-Path $outputRoot 'host.app.manifest'
    $hostManifest.Save($generatedHostManifest)
    $hostProject = Join-Path $repoRoot `
        'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
    & $dotnet build $hostProject -c Release --no-restore --nologo `
        "-p:ApplicationManifest=$generatedHostManifest"
    if ($LASTEXITCODE -ne 0) { throw 'Release host build failed.' }
}

$hostExecutable = Join-Path $externalRoot 'LongBetterWindows.Host.exe'
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "External host executable was not found: $hostExecutable"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} `
    'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Visual Studio discovery tool was not found.'
}
$visualStudioRoot = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioRoot)) {
    throw 'Visual Studio C++ x64 build tools were not found.'
}
$msbuild = Join-Path $visualStudioRoot 'MSBuild\Current\Bin\MSBuild.exe'
$dumpbin = Get-ChildItem -LiteralPath (Join-Path $visualStudioRoot 'VC\Tools\MSVC') `
    -Filter dumpbin.exe -File -Recurse |
    Where-Object { $_.FullName -match '\\bin\\Hostx64\\x64\\dumpbin\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace($dumpbin)) {
    throw 'Visual Studio MSBuild or x64 dumpbin was not found.'
}

$shellDirectory = Join-Path $externalRoot 'ShellExtension'
[IO.Directory]::CreateDirectory($shellDirectory) | Out-Null
$nativeBuildRoot = Join-Path $outputRoot 'native-build'
[IO.Directory]::CreateDirectory($nativeBuildRoot) | Out-Null
$shellProject = Join-Path $repoRoot `
    'src\LongBetterWindows.ShellExtension\LongBetterWindows.ShellExtension.vcxproj'
& $msbuild $shellProject /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
    ("/p:OutDir=$nativeBuildRoot\") `
    ("/p:IntDir=$(Join-Path $nativeBuildRoot 'obj')\") `
    /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Native Explorer command build failed.' }

$builtShellDll = Join-Path $nativeBuildRoot 'LongBetterWindows.ShellExtension.dll'
$shellDll = Join-Path $shellDirectory 'LongBetterWindows.ShellExtension.dll'
if (-not (Test-Path -LiteralPath $builtShellDll -PathType Leaf)) {
    throw "Native Explorer command DLL was not produced: $builtShellDll"
}
Copy-Item -LiteralPath $builtShellDll -Destination $shellDll -Force
$exports = & $dumpbin /nologo /exports $shellDll
$exportText = $exports -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0 -or
    $exportText -notmatch '\bDllGetClassObject\b' -or
    $exportText -notmatch '\bDllCanUnloadNow\b') {
    throw 'Native Explorer command DLL does not expose the required COM exports.'
}
$headerText = (& $dumpbin /nologo /headers $shellDll) -join `
    [Environment]::NewLine
if ($LASTEXITCODE -ne 0 -or $headerText -notmatch 'machine \(x64\)') {
    throw 'Native Explorer command DLL is not an x64 image.'
}

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdkDirectory = Get-ChildItem -LiteralPath $kitsRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [Version]$_.Name } -Descending |
    Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')
    } |
    Select-Object -First 1
if ($null -eq $sdkDirectory) {
    throw 'Windows SDK MakeAppx x64 tool was not found.'
}
$makeAppx = Join-Path $sdkDirectory.FullName 'x64\makeappx.exe'

$stageRoot = Join-Path $outputRoot 'stage'
$assetsRoot = Join-Path $stageRoot 'Assets'
[IO.Directory]::CreateDirectory($assetsRoot) | Out-Null
$manifestSource = Join-Path $repoRoot `
    'src\LongBetterWindows.Host\Package\appxmanifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestSource -Raw -Encoding UTF8
$namespace = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespace.AddNamespace(
    'foundation',
    'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identity = $manifest.SelectSingleNode('/foundation:Package/foundation:Identity', $namespace)
if ($null -eq $identity) { throw 'Sparse package manifest Identity is missing.' }
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', $Version)
$manifestPath = Join-Path $stageRoot 'AppxManifest.xml'
$settings = [Xml.XmlWriterSettings]::new()
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create($manifestPath, $settings)
try { $manifest.Save($writer) } finally { $writer.Dispose() }

Add-Type -AssemblyName System.Drawing
$sourceIconPath = Join-Path $repoRoot 'Assets\app.ico'
$sourceIcon = [Drawing.Icon]::new($sourceIconPath)
try {
    $logos = [ordered]@{
        'StoreLogo.png' = 50
        'Square44x44Logo.png' = 44
        'Square150x150Logo.png' = 150
    }
    foreach ($logo in $logos.GetEnumerator()) {
        $bitmap = [Drawing.Bitmap]::new($logo.Value, $logo.Value)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = `
                    [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawIcon(
                    $sourceIcon,
                    [Drawing.Rectangle]::new(0, 0, $logo.Value, $logo.Value))
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save(
                (Join-Path $assetsRoot $logo.Key),
                [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceIcon.Dispose()
}

$packagePath = Join-Path $outputRoot "LongBetterWindows.Sparse.$Version.x64.msix"
& $makeAppx pack /d $stageRoot /p $packagePath /o /nv
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx sparse package validation failed.' }

$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
$trackedStatus = & git -C $repoRoot status --porcelain --untracked-files=no
$report = [ordered]@{
    schema_version = 1
    classification = 'unsigned_sparse_package_build'
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    source_commit = $sourceCommit
    tracked_source_clean = [string]::IsNullOrWhiteSpace(($trackedStatus -join ''))
    architecture = 'x64'
    package_version = $Version
    publisher = $Publisher
    external_location = $externalRoot
    host_executable = $hostExecutable
    host_sha256 = (Get-FileHash -LiteralPath $hostExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    shell_extension = [ordered]@{
        path = $shellDll
        sha256 = (Get-FileHash -LiteralPath $shellDll -Algorithm SHA256).Hash.ToLowerInvariant()
        required_com_exports_verified = $true
        machine_verified = 'x64'
    }
    package = [ordered]@{
        path = $packagePath
        bytes = (Get-Item -LiteralPath $packagePath).Length
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        signed = $false
        installation_attempted = $false
    }
    windows_sdk = $sdkDirectory.Name
    passed = $true
}
$reportPath = Join-Path $outputRoot 'sparse-package-build.json'
$report | ConvertTo-Json -Depth 5 | Set-Content `
    -LiteralPath $reportPath -Encoding UTF8

Write-Output 'Unsigned sparse package build passed.'
Write-Output "Package: $packagePath"
Write-Output "Report: $reportPath"
Write-Output 'Signing and package registration were intentionally not attempted.'
