#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Runs the single repository-level automated validation entry point.
.DESCRIPTION
  Release mode delegates the complete gate set to verify-final-closure.ps1.
  Debug mode is an explicit developer-only build and test loop and never
  represents release eligibility.
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputPath,
    [switch]$AllowDirty,
    [switch]$RequireReleaseEligible,
    [switch]$NoConsoleReport
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = $PSScriptRoot
$finalClosureScript = Join-Path $repositoryRoot "verify-final-closure.ps1"
$evidenceIo = Join-Path $repositoryRoot "release-evidence-io.ps1"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path -LiteralPath $finalClosureScript -PathType Leaf)) {
    throw "Final closure pipeline was not found: $finalClosureScript"
}
if (-not (Test-Path -LiteralPath $evidenceIo -PathType Leaf)) {
    throw "Release evidence writer was not found: $evidenceIo"
}
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw ".NET SDK was not found. Install the .NET 8 SDK."
    }
    $dotnet = $dotnetCommand.Source
}

. $evidenceIo

function Resolve-OutputPath([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $repositoryRoot $PathValue))
}

function Get-SourceIdentity {
    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve the source commit."
    }
    $trackedStatus = @(& git -C $repositoryRoot status `
        --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the tracked worktree."
    }
    return [PSCustomObject]@{
        Commit = $commit
        Dirty = $trackedStatus.Count -gt 0
    }
}

$resolvedOutput = Resolve-OutputPath $OutputPath
$temporaryRoot = $null
if ([string]::IsNullOrWhiteSpace($resolvedOutput)) {
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
        ("long-automated-closure-{0}" -f [guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $resolvedOutput = Join-Path $temporaryRoot "automated-closure.json"
}

try {
    if ($Configuration -eq "Release") {
        $arguments = @{
            OutputPath = $resolvedOutput
        }
        if ($AllowDirty) {
            $arguments.AllowDirty = $true
        }
        if ($RequireReleaseEligible) {
            $arguments.RequireReleaseEligible = $true
        }

        & $finalClosureScript @arguments | Out-Null
        $exitCode = $LASTEXITCODE
        if (-not (Test-Path -LiteralPath $resolvedOutput -PathType Leaf)) {
            throw "Final closure did not create its machine-readable report."
        }
        if (-not $NoConsoleReport) {
            Get-Content -LiteralPath $resolvedOutput -Raw -Encoding UTF8
        }
        exit $exitCode
    }

    $identity = Get-SourceIdentity
    $gates = [System.Collections.Generic.List[object]]::new()
    $commands = @(
        [PSCustomObject]@{
            Id = "dependency-restore"
            Arguments = @("restore", "LongBetterWindows.sln", "--nologo")
        },
        [PSCustomObject]@{
            Id = "debug-build"
            Arguments = @(
                "build", "LongBetterWindows.sln", "--configuration", "Debug",
                "--no-restore", "--nologo")
        },
        [PSCustomObject]@{
            Id = "debug-tests"
            Arguments = @(
                "test", "tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj",
                "--configuration", "Debug", "--no-build", "--nologo")
        }
    )

    Push-Location $repositoryRoot
    try {
        foreach ($command in $commands) {
            & $dotnet @($command.Arguments)
            $commandExitCode = $LASTEXITCODE
            $gates.Add([ordered]@{
                id = $command.Id
                status = if ($commandExitCode -eq 0) { "passed" } else { "failed" }
                exit_code = $commandExitCode
            })
            if ($commandExitCode -ne 0) {
                break
            }
        }
    }
    finally {
        Pop-Location
    }

    $failedCount = @($gates | Where-Object status -eq "failed").Count
    $notRunCount = $commands.Count - $gates.Count
    foreach ($command in @($commands | Select-Object -Skip $gates.Count)) {
        $gates.Add([ordered]@{
            id = $command.Id
            status = "not_run"
            exit_code = $null
        })
    }
    $report = [ordered]@{
        schema_version = 1
        classification = "development_automated_closure"
        generated_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
        configuration = "Debug"
        source_commit = $identity.Commit
        source_dirty = $identity.Dirty
        release_eligible = $false
        automated_acceptance = [ordered]@{
            automated_gate_count = $commands.Count
            passed_gate_count = @($gates | Where-Object status -eq "passed").Count
            failed_gate_count = $failedCount
            environment_blocked_gate_count = 0
            not_run_gate_count = $notRunCount
            not_applicable_gate_count = 0
            gates = $gates
        }
    }
    Write-NewJsonFileAtomically `
        -Path $resolvedOutput `
        -Value $report `
        -Depth 8 `
        -Label "Development automated closure report"
    if (-not $NoConsoleReport) {
        Get-Content -LiteralPath $resolvedOutput -Raw -Encoding UTF8
    }
    if ($failedCount -gt 0 -or $notRunCount -gt 0) {
        exit 1
    }
    exit 0
}
finally {
    if ($null -ne $temporaryRoot -and
        (Test-Path -LiteralPath $temporaryRoot -PathType Container)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
