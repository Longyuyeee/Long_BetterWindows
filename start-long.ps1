param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string[]]$ApplicationArguments = @(),
    [ValidateRange(0, 30000)]
    [int]$QualityIdleMilliseconds = 0,
    [switch]$NoBuild,
    [switch]$Wait,
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = $PSScriptRoot
$solution = Join-Path $repositoryRoot "LongBetterWindows.sln"
$project = Join-Path $repositoryRoot `
    "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj"
$hostDirectory = Join-Path $repositoryRoot `
    "src\LongBetterWindows.Host\bin\$Configuration\net8.0-windows"
$hostExecutable = Join-Path $hostDirectory `
    "LongBetterWindows.Host.exe"

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        $dotnet = $dotnetCommand.Source
    }
}
$dotnetAvailable = Test-Path -LiteralPath $dotnet -PathType Leaf
$projectAvailable = Test-Path -LiteralPath $project -PathType Leaf
$solutionAvailable = Test-Path -LiteralPath $solution -PathType Leaf

if ($PreflightOnly) {
    [ordered]@{
        schema_version = 1
        classification = "long_assistant_start_preflight"
        configuration = $Configuration
        repository_root = $repositoryRoot
        dotnet_available = $dotnetAvailable
        solution_available = $solutionAvailable
        project_available = $projectAvailable
        existing_host_available = Test-Path -LiteralPath `
            $hostExecutable -PathType Leaf
        no_build = [bool]$NoBuild
        ready = $dotnetAvailable -and $solutionAvailable `
            -and $projectAvailable
    } | ConvertTo-Json -Depth 4
    exit 0
}

if (-not $dotnetAvailable) {
    throw "dotnet CLI was not found. Install the .NET 8 SDK."
}
if (-not $solutionAvailable -or -not $projectAvailable) {
    throw "Run this script from a complete Long Assistant repository."
}

Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        Write-Host "Building Long Assistant ($Configuration)..."
        & $dotnet build $solution -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
        throw "Host executable was not found: $hostExecutable"
    }

    $startParameters = @{
        FilePath = $hostExecutable
        WorkingDirectory = $hostDirectory
        PassThru = $true
    }
    $effectiveArguments = @($ApplicationArguments)
    if ($QualityIdleMilliseconds -gt 0) {
        $effectiveArguments += @(
            "--quality-idle-ms",
            [string]$QualityIdleMilliseconds)
    }
    if ($effectiveArguments.Count -gt 0) {
        $startParameters.ArgumentList = $effectiveArguments
    }
    $process = Start-Process @startParameters
    Write-Host "Long Assistant started. PID=$($process.Id)"

    if ($Wait) {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Long Assistant exited with code $($process.ExitCode)."
        }
        Write-Host "Long Assistant exited normally."
    }
}
finally {
    Pop-Location
}
