param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$arguments = @(
    'run',
    '--project', (Join-Path $root 'tools/LongBetterWindows.PluginCatalogGenerator'),
    '--configuration', 'Release',
    '--',
    '--root', $root
)
if ($Check) { $arguments += '--check' }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
