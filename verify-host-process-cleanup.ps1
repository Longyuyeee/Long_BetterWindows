param(
    [string]$HostExecutable = "",
    [string]$OutputPath = "",
    [ValidateRange(1000, 30000)]
    [int]$IdleMilliseconds = 5000,
    [ValidateRange(1, 30)]
    [int]$CleanupTimeoutSeconds = 10,
    [ValidateRange(50, 1000)]
    [int]$PollMilliseconds = 100
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($HostExecutable)) {
    $HostExecutable = Join-Path $root `
        "src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root `
        "artifacts/quality/host-process-cleanup/host-process-cleanup.json"
}
$HostExecutable = [IO.Path]::GetFullPath($HostExecutable)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $HostExecutable -PathType Leaf)) {
    throw "Release host executable was not found: $HostExecutable"
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Host process cleanup output already exists: $OutputPath"
}
if (@(Get-Process -Name "LongBetterWindows.Host" `
        -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "Close existing LongBetterWindows.Host processes before this probe."
}

function Get-ProcessSnapshot {
    @(Get-CimInstance Win32_Process | ForEach-Object {
        [pscustomobject]@{
            ProcessId = [int]$_.ProcessId
            ParentProcessId = [int]$_.ParentProcessId
            Name = [string]$_.Name
            ExecutablePath = [string]$_.ExecutablePath
            CreationDate = if ($_.CreationDate) {
                ([DateTimeOffset]$_.CreationDate).ToUniversalTime().ToString("O")
            } else {
                ""
            }
        }
    })
}

function Get-Descendants {
    param(
        [int]$RootProcessId,
        [object[]]$Snapshot
    )

    $parents = [Collections.Generic.HashSet[int]]::new()
    $null = $parents.Add($RootProcessId)
    $result = [Collections.Generic.List[object]]::new()
    do {
        $added = $false
        foreach ($item in $Snapshot) {
            if ($parents.Contains($item.ParentProcessId) `
                -and -not $parents.Contains($item.ProcessId)) {
                $null = $parents.Add($item.ProcessId)
                $result.Add($item)
                $added = $true
            }
        }
    } while ($added)
    @($result)
}

function Get-IdentityKey {
    param([object]$ProcessInfo)
    "$($ProcessInfo.ProcessId)|$($ProcessInfo.CreationDate)"
}

function Stop-HostProcessTree {
    param([Diagnostics.Process]$Process)
    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }
    & taskkill.exe /PID $Process.Id /T /F 2>&1 | Out-Null
    $null = $Process.WaitForExit(5000)
}

$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    throw "Unable to resolve the source commit."
}
$sourceDirty = -not [string]::IsNullOrWhiteSpace(
    ((& git -C $root status --porcelain --untracked-files=no) -join "`n"))

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$startupReportPath = Join-Path $directory "host-startup.json"
$shutdownReportPath = Join-Path $directory "host-shutdown.json"
if (Test-Path -LiteralPath $shutdownReportPath) {
    throw "Host shutdown evidence already exists: $shutdownReportPath"
}
$arguments = @(
    "--quality-open-plugin-runtime", "com.long.base64",
    "--quality-idle-ms", $IdleMilliseconds.ToString(),
    "--quality-startup-report", $startupReportPath,
    "--quality-shutdown-report", $shutdownReportPath,
    "--quality-source-commit", $sourceCommit,
    "--theme", "dark",
    "--language", "zh-CN"
)
$observed = @{}
$startedAt = [DateTimeOffset]::UtcNow
$hostProcess = Start-Process -FilePath $HostExecutable `
    -ArgumentList $arguments `
    -WorkingDirectory $root `
    -PassThru
try {
    $hostDeadline = [DateTimeOffset]::UtcNow.AddSeconds(45)
    while (-not $hostProcess.HasExited) {
        foreach ($child in Get-Descendants `
                $hostProcess.Id (Get-ProcessSnapshot)) {
            $observed[(Get-IdentityKey $child)] = $child
        }
        if ([DateTimeOffset]::UtcNow -ge $hostDeadline) {
            Stop-HostProcessTree $hostProcess
            throw "Host process cleanup probe timed out."
        }
        Start-Sleep -Milliseconds $PollMilliseconds
        $hostProcess.Refresh()
    }
    $hostExitCode = $hostProcess.ExitCode

    $cleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(
        $CleanupTimeoutSeconds)
    do {
        $snapshot = Get-ProcessSnapshot
        foreach ($child in Get-Descendants $hostProcess.Id $snapshot) {
            $observed[(Get-IdentityKey $child)] = $child
        }
        $current = @{}
        foreach ($item in $snapshot) {
            $current[(Get-IdentityKey $item)] = $item
        }
        $remaining = @($observed.Keys | Where-Object {
            $current.ContainsKey($_)
        } | ForEach-Object { $current[$_] })
        if ($remaining.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $cleanupDeadline)

    $observedProcesses = @($observed.Values | Sort-Object `
        CreationDate, ProcessId)
    $remainingProcesses = @($remaining | Sort-Object `
        CreationDate, ProcessId)
    $webViewProcesses = @($observedProcesses | Where-Object {
        $_.Name -ieq "msedgewebview2.exe"
    })
    $workerProcesses = @($observedProcesses | Where-Object {
        $_.Name -like "long-plugin-worker*"
    })
    $startupReportExists = Test-Path -LiteralPath $startupReportPath
    $shutdownReportExists = Test-Path -LiteralPath $shutdownReportPath
    $shutdownReportValid = $false
    $shutdownReportIssues = [Collections.Generic.List[string]]::new()
    if ($shutdownReportExists) {
        try {
            $shutdownReport = Get-Content -Raw -LiteralPath `
                $shutdownReportPath | ConvertFrom-Json
            $expectedStepNames = @(
                "broker",
                "plugins",
                "plugin_runtime",
                "host_services"
            )
            $actualStepNames = @($shutdownReport.steps | ForEach-Object {
                [string]$_.name
            })
            $expectedHostHash = (
                Get-FileHash -LiteralPath $HostExecutable -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            if ([int]$shutdownReport.schema_version -ne 1) {
                $shutdownReportIssues.Add("schema_version")
            }
            if ([string]$shutdownReport.source_commit -ne $sourceCommit) {
                $shutdownReportIssues.Add("source_commit")
            }
            if ([string]$shutdownReport.host_executable_sha256 `
                    -ne $expectedHostHash) {
                $shutdownReportIssues.Add("host_executable_sha256")
            }
            if ([int]$shutdownReport.host_process_id -ne $hostProcess.Id) {
                $shutdownReportIssues.Add("host_process_id")
            }
            if ([int]$shutdownReport.host_exit_code -ne $hostExitCode) {
                $shutdownReportIssues.Add("host_exit_code")
            }
            if (($actualStepNames -join "|") `
                    -ne ($expectedStepNames -join "|")) {
                $shutdownReportIssues.Add("step_names")
            }
            foreach ($step in @($shutdownReport.steps)) {
                if ([string]$step.status -ne "passed") {
                    $shutdownReportIssues.Add("step_status:$($step.name)")
                }
                if ([double]$step.elapsed_ms -lt 0) {
                    $shutdownReportIssues.Add("step_elapsed:$($step.name)")
                }
                if ([string]$step.error_category -ne "none") {
                    $shutdownReportIssues.Add("step_error:$($step.name)")
                }
            }
            if (-not [bool]$shutdownReport.passed) {
                $shutdownReportIssues.Add("passed")
            }
            $shutdownReportValid = $shutdownReportIssues.Count -eq 0
        }
        catch {
            $shutdownReportIssues.Add("invalid_json")
        }
    } else {
        $shutdownReportIssues.Add("missing")
    }
    $passed = $hostExitCode -eq 0 `
        -and $startupReportExists `
        -and $shutdownReportValid `
        -and $remainingProcesses.Count -eq 0
    $report = [ordered]@{
        schema_version = 3
        captured_at = [DateTimeOffset]::UtcNow.ToString("O")
        classification = "development_host_process_cleanup"
        source_commit = $sourceCommit
        source_dirty = $sourceDirty
        host_executable = $HostExecutable
        host_executable_sha256 = (
            Get-FileHash -LiteralPath $HostExecutable -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        startup_report = $startupReportPath
        startup_report_exists = $startupReportExists
        shutdown_report = $shutdownReportPath
        shutdown_report_exists = $shutdownReportExists
        shutdown_report_valid = $shutdownReportValid
        shutdown_report_issues = @($shutdownReportIssues)
        host_process_id = $hostProcess.Id
        host_exit_code = $hostExitCode
        elapsed_ms = [Math]::Round(
            ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds,
            1)
        cleanup_timeout_seconds = $CleanupTimeoutSeconds
        observed_descendant_count = $observedProcesses.Count
        observed_webview2_count = $webViewProcesses.Count
        observed_plugin_worker_count = $workerProcesses.Count
        remaining_descendant_count = $remainingProcesses.Count
        passed = $passed
        observed_descendants = $observedProcesses
        remaining_descendants = $remainingProcesses
    }
    $json = $report | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText(
        $OutputPath,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
    Write-Host "Host process cleanup report: $OutputPath"
    Write-Host "Observed descendants: $($observedProcesses.Count)"
    Write-Host "WebView2 descendants: $($webViewProcesses.Count)"
    Write-Host "Plugin Worker descendants: $($workerProcesses.Count)"
    Write-Host "Remaining descendants: $($remainingProcesses.Count)"
    Write-Host "Shutdown report valid: $shutdownReportValid"
    if (-not $passed) {
        throw "Host process cleanup probe failed."
    }
}
finally {
    if (-not $hostProcess.HasExited) {
        Stop-HostProcessTree $hostProcess
    }
    $hostProcess.Dispose()
}
