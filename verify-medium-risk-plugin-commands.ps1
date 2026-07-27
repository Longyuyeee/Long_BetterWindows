param(
    [string]$OutputPath = "artifacts/quality/medium-risk-plugin-commands/report.json",
    [string]$HostDirectory = "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "verify-low-risk-plugin-commands.ps1") `
    -CasesPath "docs/medium-risk-plugin-command-cases.json" `
    -HostDirectory $HostDirectory `
    -OutputPath $OutputPath `
    -TimeoutSeconds $TimeoutSeconds
exit $LASTEXITCODE
