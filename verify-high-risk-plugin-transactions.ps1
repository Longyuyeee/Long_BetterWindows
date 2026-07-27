param(
    [string]$CasesPath = "docs/high-risk-plugin-transaction-cases.json",
    [string]$HostDirectory = "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"

$arguments = @{
    CasesPath = $CasesPath
    HostDirectory = $HostDirectory
    TimeoutSeconds = $TimeoutSeconds
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $arguments.OutputPath = $OutputPath
}

& (Join-Path $PSScriptRoot "verify-low-risk-plugin-commands.ps1") @arguments
exit $LASTEXITCODE
