#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Measure idle host memory with the 25 distinct built-in Long plugins.
#>
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(3,20)] [int] $Samples = 5,
    [ValidateRange(1000,15000)] [int] $IdleMilliseconds = 2500,
    [ValidateRange(100,1024)] [double] $WorkingSetLimitMB = 200,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw "Memory evidence directory already exists: $outputRoot" }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw 'dotnet CLI was not found.' }
    $dotnet = $command.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
if (-not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Memory probe Release build failed.' }
}
$hostRoot = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows'
$executable = Join-Path $hostRoot 'LongBetterWindows.Host.exe'
$pluginRoot = Join-Path $hostRoot 'Plugins'
$manifestFiles = @(Get-ChildItem -LiteralPath $pluginRoot -Filter manifest.json -File -Recurse)
$manifests = @($manifestFiles | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
})
$uniqueIds = @($manifests.id | Sort-Object -Unique)
if (@(Get-ChildItem -LiteralPath $pluginRoot -Directory).Count -ne 25 `
    -or $manifestFiles.Count -ne 25 -or $uniqueIds.Count -ne 25) {
    throw "Memory probe requires 25 distinct built-in plugins: manifests=$($manifestFiles.Count), unique=$($uniqueIds.Count)"
}

$results = @()
for ($index = 1; $index -le $Samples; $index++) {
    $runRoot = Join-Path $outputRoot ("run-{0:D2}" -f $index)
    [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $process = Start-Process -FilePath $executable `
        -ArgumentList @('--theme','dark','--plugins-dir',$pluginRoot,'--quality-idle-ms',$IdleMilliseconds.ToString()) `
        -WorkingDirectory $runRoot -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force
        throw "Memory sample $index timed out."
    }
    if ($process.ExitCode -ne 0) { throw "Memory sample $index exited with code $($process.ExitCode)." }
    $log = Get-ChildItem -LiteralPath (Join-Path $runRoot 'logs') -Filter '*.txt' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $log) { throw "Memory sample $index did not create a log." }
    $line = Get-Content -LiteralPath $log.FullName -Encoding UTF8 `
        | Where-Object { $_ -match 'Plugins=\d+, Commands=\d+, WorkingSetMB=' } `
        | Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace([string]$line)) {
        throw "Memory sample $index did not contain a probe record."
    }
    $probeMatch = [regex]::Match(
        [string]$line,
        'Plugins=(\d+), Commands=(\d+), WorkingSetMB=([\d.]+), PrivateMB=([\d.]+)')
    if (-not $probeMatch.Success) {
        throw "Memory sample $index did not contain a valid probe record."
    }
    $results += [pscustomobject][ordered]@{
        sample = $index
        plugins = [int]$probeMatch.Groups[1].Value
        commands = [int]$probeMatch.Groups[2].Value
        working_set_mb = [double]$probeMatch.Groups[3].Value
        private_mb = [double]$probeMatch.Groups[4].Value
        exit_code = $process.ExitCode
    }
}

$workingSets = @($results.working_set_mb | Sort-Object)
$middle = [int][Math]::Floor($workingSets.Count / 2)
$median = if ($workingSets.Count % 2) { $workingSets[$middle] } else { ($workingSets[$middle - 1] + $workingSets[$middle]) / 2 }
$maximum = ($workingSets | Measure-Object -Maximum).Maximum
$passed = @($results | Where-Object { $_.plugins -ne 25 -or $_.commands -lt 37 }).Count -eq 0 `
    -and $maximum -lt $WorkingSetLimitMB
$report = [ordered]@{
    schema_version = 1
    measured_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'distinct_builtin_plugin_memory_probe'
    plugin_count = 25
    unique_plugin_ids = $uniqueIds
    command_count_minimum = 37
    samples = $results
    median_working_set_mb = $median
    maximum_working_set_mb = $maximum
    working_set_limit_mb = $WorkingSetLimitMB
    passed = $passed
}
$reportPath = Join-Path $outputRoot 'plugin-memory-report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "25-plugin memory probe: median=$median MB, max=$maximum MB, passed=$passed"
Write-Output "Report: $reportPath"
if (-not $passed) { throw '25-plugin memory probe did not meet the release threshold.' }
