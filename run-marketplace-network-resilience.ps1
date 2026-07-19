#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Network resilience output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet CLI was not found.' }
    $dotnet = $dotnetCommand.Source
}

function Get-DirectoryFingerprint([string] $path) {
    $records = @()
    if (Test-Path -LiteralPath $path) {
        $root = [IO.Path]::GetFullPath($path).TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse | Sort-Object FullName) {
            $relative = $file.FullName.Substring($root.Length).Replace('\', '/')
            $records += "$relative|$($file.Length)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)"
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
        return [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
    }
    finally { $sha.Dispose() }
}

$testsProject = Join-Path $repoRoot 'tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj'
$releasePlugins = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\Plugins'
$resultsRoot = Join-Path $outputRoot 'test-results'
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null
$before = Get-DirectoryFingerprint $releasePlugins
if (-not $NoBuild) {
    & $dotnet build $testsProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Network resilience Release build failed.' }
}

& $dotnet test $testsProject -c Release --no-build `
    --filter 'FullyQualifiedName~MarketplaceTransportTests' `
    --logger 'trx;LogFileName=network-resilience.trx' `
    --results-directory $resultsRoot
$testExitCode = $LASTEXITCODE
$trxPath = Join-Path $resultsRoot 'network-resilience.trx'
if (-not (Test-Path -LiteralPath $trxPath)) {
    throw 'Network resilience TRX report was not generated.'
}
[xml]$trx = Get-Content -LiteralPath $trxPath -Raw
$results = @($trx.TestRun.Results.UnitTestResult)
function Test-Passed([string] $name) {
    return @($results | Where-Object {
        $_.testName -like "*$name*" -and $_.outcome -eq 'Passed'
    }).Count -gt 0
}

$after = Get-DirectoryFingerprint $releasePlugins
$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'marketplace_network_resilience_gate'
    tests_total = $results.Count
    tests_passed = @($results | Where-Object outcome -eq 'Passed').Count
    transient_retry = Test-Passed 'Downloader_RetriesTransientFailureAndCleansStaleTemporaryFile'
    stale_partial_cleanup = Test-Passed 'Downloader_RetriesTransientFailureAndCleansStaleTemporaryFile'
    concurrent_download_coalescing = Test-Passed 'Downloader_CoalescesConcurrentRequestsForTheSamePackage'
    concurrent_install_serialization = Test-Passed 'ConcurrentInstallRequests_AreSerializedWithoutTransactionResidue'
    bounded_timeout = Test-Passed 'Downloader_ReportsTimeoutAfterBoundedAttemptsAndRemovesPartials'
    offline_catalog_fallback = Test-Passed 'RemoteRepository_UsesLastValidCacheWhenNetworkFails'
    invalid_hash_cleanup = Test-Passed 'Downloader_HashMismatchRejectsAndRemovesTemporaryFile'
    interrupted_transaction_recovery = Test-Passed 'InterruptedTransactions_RestoreOnlyUncommittedStateAndRemoveJournals'
    release_plugins_fingerprint_before = $before
    release_plugins_fingerprint_after = $after
    release_plugins_unchanged = $before -eq $after
    passed = $false
}
$report.passed = $testExitCode -eq 0 `
    -and $report.transient_retry `
    -and $report.stale_partial_cleanup `
    -and $report.concurrent_download_coalescing `
    -and $report.concurrent_install_serialization `
    -and $report.bounded_timeout `
    -and $report.offline_catalog_fallback `
    -and $report.invalid_hash_cleanup `
    -and $report.interrupted_transaction_recovery `
    -and $report.release_plugins_unchanged
$report | ConvertTo-Json -Depth 6 | Set-Content `
    -LiteralPath (Join-Path $outputRoot 'network-resilience.json') -Encoding UTF8

if (-not $report.passed) { throw 'Marketplace network resilience gate failed.' }
Write-Output 'Marketplace network resilience gate passed.'
Write-Output "Report: $(Join-Path $outputRoot 'network-resilience.json')"
