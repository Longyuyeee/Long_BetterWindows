#!/usr/bin/env pwsh
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw '未找到 dotnet CLI。请安装 .NET 8 SDK 或更新脚本中的路径。'
    }
    $dotnet = $dotnetCommand.Source
}

Push-Location $repoRoot
try {
    & $dotnet build 'LongBetterWindows.sln' -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $dotnet test 'tests\LongBetterWindows.Tests\LongBetterWindows.Tests.csproj' `
        -c $Configuration --no-build --logger 'console;verbosity=minimal'
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
