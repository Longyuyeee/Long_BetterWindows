#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [switch] $NoBuild,
    [switch] $SkipWeb,
    [switch] $SkipRuntimeMatrix
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "LPWP compatibility output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$baselinePath = Join-Path $repoRoot 'docs\protocol\lpwp-compatibility-baseline.json'
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json

if (-not $NoBuild) {
    & $dotnet build (Join-Path $repoRoot 'LongBetterWindows.sln') `
        --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'LPWP Release build failed.' }
}

& $dotnet test `
    (Join-Path $repoRoot 'tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj') `
    --configuration Release --no-build `
    --filter 'FullyQualifiedName~PluginIpc|FullyQualifiedName~PluginBroker|FullyQualifiedName~BrokerSettingsDiagnostics|FullyQualifiedName~LpwpCompatibilityGate'
if ($LASTEXITCODE -ne 0) { throw 'LPWP .NET contract tests failed.' }

if (-not $SkipWeb) {
    & npm ci --prefix (Join-Path $repoRoot 'sdk\web')
    if ($LASTEXITCODE -ne 0) { throw 'LPWP Web SDK dependency restore failed.' }
    & npm test --prefix (Join-Path $repoRoot 'sdk\web')
    if ($LASTEXITCODE -ne 0) { throw 'LPWP Web SDK tests failed.' }
}

if (-not $SkipRuntimeMatrix) {
    & (Join-Path $repoRoot 'verify-plugin-runtime-matrix.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'LPWP runtime matrix failed.' }
}

$referenceOutput = Join-Path $outputRoot 'reference-widget'
& (Join-Path $repoRoot 'build-reference-widget.ps1') -OutputDir $referenceOutput
if ($LASTEXITCODE -ne 0) { throw 'LPWP reference widget build failed.' }

$packageOutput = Join-Path $outputRoot 'packages'
[IO.Directory]::CreateDirectory($packageOutput) | Out-Null
& $dotnet pack `
    (Join-Path $repoRoot 'src\LongBetterWindows.PluginIpc\LongBetterWindows.PluginIpc.csproj') `
    --configuration Release --no-build --output $packageOutput
if ($LASTEXITCODE -ne 0) { throw 'LPWP IPC package build failed.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$nupkg = Get-ChildItem -LiteralPath $packageOutput -Filter '*.nupkg' | Select-Object -First 1
$archive = [IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($fixture in $baseline.fixture_files) {
        if ("fixtures/ipc/$fixture" -notin $entries) {
            throw "IPC NuGet is missing Golden Fixture: $fixture"
        }
    }
}
finally {
    $archive.Dispose()
}

$referencePackage = Get-ChildItem -LiteralPath $referenceOutput -Filter '*.lpak' | Select-Object -First 1
$report = [ordered]@{
    schema_version = 1
    generated_utc = [DateTime]::UtcNow.ToString('O')
    source_commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    protocol = $baseline.protocol
    lpwp_version = $baseline.lpwp_version
    release_build = -not $NoBuild
    dotnet_contract_tests = $true
    web_sdk_tests = -not $SkipWeb
    runtime_matrix = -not $SkipRuntimeMatrix
    ipc_package = $nupkg.Name
    ipc_package_sha256 = (Get-FileHash -LiteralPath $nupkg.FullName -Algorithm SHA256).Hash
    reference_widget = $referencePackage.Name
    reference_widget_sha256 = (Get-FileHash -LiteralPath $referencePackage.FullName -Algorithm SHA256).Hash
    fixture_count = @($baseline.fixture_files).Count
    external_long_grid_e2e = $false
    release_eligible = $false
}
$reportPath = Join-Path $outputRoot 'lpwp-compatibility-report.json'
[IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))
Write-Host "LPWP compatibility verification completed: $reportPath"
