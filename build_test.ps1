#!/usr/bin/env pwsh
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $OutputPath,
    [switch] $AllowDirty,
    [switch] $RequireReleaseEligible
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$automatedClosure = Join-Path $repoRoot 'invoke-automated-closure.ps1'

Push-Location $repoRoot
try {
    $arguments = @{
        Configuration = $Configuration
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $arguments.OutputPath = $OutputPath
    }
    if ($AllowDirty) {
        $arguments.AllowDirty = $true
    }
    if ($RequireReleaseEligible) {
        $arguments.RequireReleaseEligible = $true
    }
    & $automatedClosure @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
