#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(10,60)] [int] $TimeoutSeconds = 45,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Workflow upgrade output directory already exists: $outputRoot"
}

$transactionRoot = Join-Path $outputRoot 'transaction-temp'
$pluginsRoot = Join-Path $transactionRoot 'plugins'
$workflowsRoot = Join-Path $transactionRoot 'workflows'
$packagesRoot = Join-Path $transactionRoot 'packages'
$pluginId = 'quality.workflow.upgrade'
$pluginDirectory = Join-Path $pluginsRoot 'quality-workflow-upgrade'
$workflowId = 'workflow.quality.plugin-upgrade'
$v2Package = Join-Path $packagesRoot 'quality-workflow-upgrade-v2.lpak'
[IO.Directory]::CreateDirectory($pluginDirectory) | Out-Null
[IO.Directory]::CreateDirectory($workflowsRoot) | Out-Null
[IO.Directory]::CreateDirectory($packagesRoot) | Out-Null

function New-PluginManifest([string] $version) {
    return [ordered]@{
        id = $pluginId
        version = $version
        name = 'Quality Workflow Upgrade'
        author = 'Long Quality'
        runtime = 'webview'
        entry_point = 'index.html'
        capabilities = @('storage.local')
        commands = @([ordered]@{
            id = 'probe'
            title = 'Upgrade Probe'
            description = 'Quality-only workflow upgrade probe'
            aliases = @('upgrade-probe')
            accepted_inputs = @('none')
            outputs = @()
            view_mode = 'form'
            priority = 1
        })
        min_host_version = '0.5.0'
        min_api_version = '1.0.0'
        min_ui_kit_version = '1.0.0'
        lifecycle = [ordered]@{
            start_with_host = $false
            default_presentation = 'embedded'
            close_behavior = 'stop'
            search_in_background = $false
        }
        default_settings = [ordered]@{ auto_start = $false }
    } | ConvertTo-Json -Depth 12
}

function Write-ZipText($archive, [string] $name, [string] $content) {
    $entry = $archive.CreateEntry($name)
    $writer = [IO.StreamWriter]::new(
        $entry.Open(),
        [Text.UTF8Encoding]::new($false))
    try { $writer.Write($content) } finally { $writer.Dispose() }
}

$v1Manifest = New-PluginManifest '1.0.0'
[IO.File]::WriteAllText(
    (Join-Path $pluginDirectory 'manifest.json'),
    $v1Manifest,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $pluginDirectory 'index.html'),
    '<!doctype html><title>Quality Workflow Upgrade v1</title>',
    [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open(
    $v2Package,
    [IO.Compression.ZipArchiveMode]::Create)
try {
    Write-ZipText $archive 'manifest.json' (New-PluginManifest '2.0.0')
    Write-ZipText $archive 'index.html' `
        '<!doctype html><title>Quality Workflow Upgrade v2</title>'
}
finally {
    $archive.Dispose()
}

$workflow = [ordered]@{
    schema_version = 3
    source = [ordered]@{
        kind = 'local_managed'
        source_id = 'local-managed'
    }
    workflow = [ordered]@{
        id = $workflowId
        name = 'Quality Plugin Upgrade Review'
        failure_mode = 'stop'
        steps = @([ordered]@{
            id = 'probe'
            effect = 'read_only'
            command = [ordered]@{
                command_key = "${pluginId}:probe"
                invocation = [ordered]@{
                    command_id = 'probe'
                    input_type = 'none'
                    text = $null
                    paths = @()
                    image_png = $null
                    arguments = [ordered]@{}
                }
                bindings = @()
            }
            compensation = $null
        })
    }
} | ConvertTo-Json -Depth 16
[IO.File]::WriteAllText(
    (Join-Path $workflowsRoot "$workflowId.workflow.json"),
    $workflow,
    [Text.UTF8Encoding]::new($false))

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet CLI was not found.' }
    $dotnet = $dotnetCommand.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$releaseRoot = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows'
$executable = Join-Path $releaseRoot 'LongBetterWindows.Host.exe'
if (-not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Workflow upgrade Release build failed.' }
}
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Host executable was not found: $executable"
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class LongWorkflowUpgradeWindows {
    delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out UIntPtr result);
    public static IntPtr FindWindow(int processId) {
        IntPtr match = IntPtr.Zero;
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) {
                match = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return match;
    }
    public static int WorkflowMessage(IntPtr window, int action) {
        uint message = RegisterWindowMessage("LongBetterWindows.Quality.WorkflowAction.v1");
        if (message == 0) return -2;
        UIntPtr result;
        IntPtr sent = SendMessageTimeout(
            window, message, new IntPtr(action), IntPtr.Zero, 0x0002, 5000, out result);
        return sent == IntPtr.Zero ? -2 : unchecked((int)result.ToUInt64());
    }
}
'@

function Wait-Until([scriptblock] $Probe, [string] $FailureMessage) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $value = & $Probe
        if ($value -is [bool]) {
            if ($value) { return $true }
        }
        elseif ($null -ne $value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $FailureMessage
}

function Send-WorkflowAction([IntPtr] $window, [int] $action, [string] $failure) {
    if ([LongWorkflowUpgradeWindows]::WorkflowMessage($window, $action) -ne 1) {
        throw $failure
    }
}

$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'isolated_workflow_plugin_upgrade'
    plugin_id = $pluginId
    workflow_id = $workflowId
    v1_review_opened = $false
    upgrade_v2 = $false
    old_review_rejected = $false
    zero_steps_executed = $false
    redacted_report_written = $false
    transaction_directories_cleaned = $false
    isolation_root_removed = $false
    passed = $false
    error = $null
    failed_stage = $null
}
$hostProcess = $null
$stage = 'startup'

try {
    $arguments = @(
        '--theme', 'dark',
        '--plugins-dir', $pluginsRoot,
        '--quality-workflows-dir', $workflowsRoot,
        '--quality-open-workflow', $workflowId,
        '--quality-workflow-upgrade-package', $v2Package
    )
    $hostProcess = Start-Process -FilePath $executable -ArgumentList $arguments `
        -WorkingDirectory $outputRoot -PassThru
    Start-Sleep -Seconds 4
    $window = Wait-Until {
        $handle = [LongWorkflowUpgradeWindows]::FindWindow($hostProcess.Id)
        if ($handle -ne [IntPtr]::Zero) { $handle }
    } 'Workflow upgrade host window did not appear.'
    Wait-Until {
        [LongWorkflowUpgradeWindows]::WorkflowMessage($window, 10) -eq 1
    } 'The v1 workflow review did not appear.' | Out-Null
    $report.v1_review_opened = $true

    $stage = 'upgrade_v2'
    Send-WorkflowAction $window 5 'The v2 plugin upgrade could not be started.'
    Wait-Until {
        $status = [LongWorkflowUpgradeWindows]::WorkflowMessage($window, 13)
        if ($status -eq -1) { throw 'The v2 plugin upgrade was rejected by the host.' }
        $status -eq 1
    } 'The v2 plugin upgrade did not complete.' | Out-Null
    $installedVersion = (
        Get-Content -Raw -Encoding utf8 (Join-Path $pluginDirectory 'manifest.json') |
            ConvertFrom-Json).version
    if ($installedVersion -ne '2.0.0') {
        throw "The isolated plugin remained at v$installedVersion."
    }
    $report.upgrade_v2 = $true

    $stage = 'confirm_stale_review'
    Send-WorkflowAction $window 3 'The stale workflow review could not be confirmed.'
    Wait-Until {
        [LongWorkflowUpgradeWindows]::WorkflowMessage($window, 14) -eq 1
    } 'The stale v1 review was not rejected after the v2 upgrade.' | Out-Null
    $report.old_review_rejected = $true

    $stage = 'audit_report'
    $executionReportFile = Wait-Until {
        Get-ChildItem -LiteralPath $workflowsRoot -Filter '*.workflow-report.json' `
            -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
    } 'The rejected execution report was not written.'
    $executionReport = Get-Content -Raw -Encoding utf8 $executionReportFile.FullName |
        ConvertFrom-Json
    if ($executionReport.status -ne 'rejected') {
        throw "The execution report status was $($executionReport.status), not rejected."
    }
    $stepEvents = @($executionReport.events | Where-Object {
        $_.kind -in @('step_started', 'step_succeeded', 'step_failed')
    })
    if ($stepEvents.Count -ne 0) {
        throw 'The rejected stale review recorded workflow step execution.'
    }
    if ($executionReport.messages_included -or
        $null -ne $executionReport.message -or
        @($executionReport.events | Where-Object { $null -ne $_.message }).Count -ne 0) {
        throw 'The rejected execution report retained sensitive messages.'
    }
    $report.zero_steps_executed = $true
    $report.redacted_report_written = $true

    $stage = 'transaction_cleanup'
    $transactionDirectories = @(Get-ChildItem -LiteralPath $transactionRoot `
        -Directory -Filter '.long-transaction-*' -ErrorAction SilentlyContinue)
    if ($transactionDirectories.Count -ne 0) {
        throw 'Plugin upgrade transaction directories were not cleaned.'
    }
    $report.transaction_directories_cleaned = $true
    $report.passed = $true
}
catch {
    $report.error = $_.Exception.Message
    $report.failed_stage = $stage
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(5000) | Out-Null
    }
    if ($report.passed -and (Test-Path -LiteralPath $transactionRoot)) {
        [IO.Directory]::Delete($transactionRoot, $true)
    }
    $report.isolation_root_removed = -not (Test-Path -LiteralPath $transactionRoot)
    if ($report.passed -and -not $report.isolation_root_removed) {
        $report.passed = $false
        $report.error = 'The isolated workflow upgrade root was not removed.'
        $report.failed_stage = 'isolation_cleanup'
    }
    $reportPath = Join-Path $outputRoot 'workflow-plugin-upgrade.json'
    $report | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $reportPath -Encoding UTF8
}

if (-not $report.passed) {
    throw "Isolated workflow plugin upgrade failed at $($report.failed_stage): $($report.error)"
}

Write-Output 'Isolated workflow plugin upgrade passed.'
Write-Output "Report: $(Join-Path $outputRoot 'workflow-plugin-upgrade.json')"
