param(
    [string]$CasesPath = "docs/low-risk-plugin-command-cases.json",
    [string]$HostDirectory = "src/LongBetterWindows.Host/bin/Release/net8.0-windows",
    [string]$OutputPath,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepositoryPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function ConvertTo-NativeArgument([string]$Value) {
    if ($null -eq $Value -or $Value.Length -eq 0) {
        return '""'
    }
    $escaped = $Value -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Get-Sha256Text([string]$Value) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hex = [BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", ""
        return $hex.ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-PropertyNames($ObjectValue) {
    if ($null -eq $ObjectValue) {
        return @()
    }
    return @($ObjectValue.PSObject.Properties |
        ForEach-Object { [string]$_.Name })
}

$casesFile = Resolve-RepositoryPath $CasesPath
$hostRoot = Resolve-RepositoryPath $HostDirectory
$hostExe = Join-Path $hostRoot "LongBetterWindows.Host.exe"
$pluginsRoot = Join-Path $hostRoot "Plugins"
if (-not (Test-Path -LiteralPath $casesFile -PathType Leaf)) {
    throw "Low-risk command cases were not found: $casesFile"
}
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
    throw "Release host executable was not found: $hostExe"
}
if (-not (Test-Path -LiteralPath $pluginsRoot -PathType Container)) {
    throw "Release plugin directory was not found: $pluginsRoot"
}

$definition = Get-Content -LiteralPath $casesFile -Raw -Encoding UTF8 |
    ConvertFrom-Json
$cases = @($definition.cases)
$pluginIds = @($cases | ForEach-Object { [string]$_.plugin_id } |
    Sort-Object -Unique)
$commandIds = @($cases | ForEach-Object { [string]$_.command_id } |
    Sort-Object -Unique)
if ($pluginIds.Count -ne [int]$definition.policy.required_plugin_count) {
    throw "Plugin coverage mismatch. Expected $($definition.policy.required_plugin_count), found $($pluginIds.Count)."
}
if ($commandIds.Count -ne [int]$definition.policy.required_command_count) {
    throw "Command coverage mismatch. Expected $($definition.policy.required_command_count), found $($commandIds.Count)."
}

$temporaryBase = [System.IO.Path]::GetFullPath((Join-Path (
    [System.IO.Path]::GetTempPath()) "LongAssistant-CommandMatrix"))
New-Item -ItemType Directory -Path $temporaryBase -Force | Out-Null
$runRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryBase (
    [Guid]::NewGuid().ToString("N"))))
New-Item -ItemType Directory -Path $runRoot | Out-Null

$caseResults = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($case in $cases) {
        $caseRoot = Join-Path $runRoot ([string]$case.id)
        $isolatedPlugins = Join-Path $caseRoot "Plugins"
        $sourcePlugin = Join-Path $pluginsRoot ([string]$case.plugin_folder)
        $targetPlugin = Join-Path $isolatedPlugins ([string]$case.plugin_folder)
        $commandReport = Join-Path $caseRoot "command-report.json"
        $fixturePath = Join-Path $caseRoot "command-fixture.json"
        New-Item -ItemType Directory -Path $isolatedPlugins -Force | Out-Null
        if (-not (Test-Path -LiteralPath $sourcePlugin -PathType Container)) {
            $failures.Add("$($case.id): release plugin folder is missing")
            continue
        }
        Copy-Item -LiteralPath $sourcePlugin -Destination $targetPlugin -Recurse

        $arguments = @(
            "--plugins-dir", $isolatedPlugins,
            "--run-command", "$($case.plugin_id):$($case.command_id)",
            "--exit-after-command",
            "--quality-command-report", $commandReport
        )
        $inputText = if ($case.PSObject.Properties.Name -contains "input") {
            [string]$case.input
        } else {
            ""
        }
        if ($case.PSObject.Properties.Name -contains "input") {
            $arguments += @("--command-text", $inputText)
        }
        if ($case.PSObject.Properties.Name -contains "fixture") {
            $case.fixture | ConvertTo-Json -Depth 12 |
                Set-Content -LiteralPath $fixturePath -Encoding UTF8
            $arguments += @("--quality-command-fixture", $fixturePath)
        }
        $quotedArguments = @($arguments | ForEach-Object {
            ConvertTo-NativeArgument ([string]$_)
        })
        $argumentLine = $quotedArguments -join " "
        $process = Start-Process -FilePath $hostExe -ArgumentList $argumentLine `
            -WorkingDirectory $hostRoot -PassThru
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill() } catch {}
            $failures.Add("$($case.id): timed out after $TimeoutSeconds seconds")
            continue
        }
        if (-not (Test-Path -LiteralPath $commandReport -PathType Leaf)) {
            $failures.Add("$($case.id): host produced no command report (exit $($process.ExitCode))")
            continue
        }

        $report = Get-Content -LiteralPath $commandReport -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $caseErrors = [System.Collections.Generic.List[string]]::new()
        $expectedExitCode = if ([bool]$case.expected_success) { 0 } else { 3 }
        if ($process.ExitCode -ne $expectedExitCode) {
            $caseErrors.Add("exit code $($process.ExitCode), expected $expectedExitCode")
        }
        if ([string]$report.command_key -ne "$($case.plugin_id):$($case.command_id)") {
            $caseErrors.Add("reported command key does not match")
        }
        if ([bool]$report.success -ne [bool]$case.expected_success) {
            $caseErrors.Add("success was $($report.success), expected $($case.expected_success)")
        }
        if ([int]$report.input_text_length -ne $inputText.Length) {
            $caseErrors.Add("input length does not match")
        }
        if ($inputText.Length -gt 0 -and
            [string]$report.input_text_sha256 -ne (Get-Sha256Text $inputText)) {
            $caseErrors.Add("input SHA-256 does not match")
        }

        $outputLength = 0
        $outputSha256 = $null
        if ($case.PSObject.Properties.Name -contains "expected_output") {
            $outputKey = [string]$case.expected_output.key
            $outputProperty = $report.outputs.PSObject.Properties[$outputKey]
            if ($null -eq $outputProperty) {
                $caseErrors.Add("required output '$outputKey' is missing")
            } else {
                $outputValue = [string]$outputProperty.Value.value
                $outputLength = $outputValue.Length
                $outputSha256 = Get-Sha256Text $outputValue
                if ($case.expected_output.PSObject.Properties.Name -contains "exact" -and
                    $outputValue -cne [string]$case.expected_output.exact) {
                    $caseErrors.Add("output '$outputKey' does not equal expected value")
                }
                if ($case.expected_output.PSObject.Properties.Name -contains "contains" -and
                    -not $outputValue.Contains([string]$case.expected_output.contains)) {
                    $caseErrors.Add("output '$outputKey' does not contain expected value")
                }
                if ($case.expected_output.PSObject.Properties.Name -contains "regex" -and
                    $outputValue -notmatch [string]$case.expected_output.regex) {
                    $caseErrors.Add("output '$outputKey' does not match expected pattern")
                }
            }
        }

        $allowedMethods = @($case.allowed_api_methods | ForEach-Object { [string]$_ })
        $usedMethods = @(Get-PropertyNames $report.api_method_calls)
        if ($null -ne $report.fixture) {
            $usedMethods = @($usedMethods +
                @(Get-PropertyNames $report.fixture.calls) |
                Sort-Object -Unique)
        }
        $unexpectedMethods = @($usedMethods | Where-Object { $_ -notin $allowedMethods })
        if ($unexpectedMethods.Count -gt 0) {
            $caseErrors.Add("unexpected host API calls: $($unexpectedMethods -join ', ')")
        }
        foreach ($requiredMethod in $allowedMethods) {
            if ($requiredMethod -notin $usedMethods) {
                $caseErrors.Add("expected read-only host API was not called: $requiredMethod")
            }
        }

        $fixtureSummary = $null
        if ($case.PSObject.Properties.Name -contains "fixture") {
            if ($null -eq $report.fixture) {
                $caseErrors.Add("quality fixture snapshot is missing")
            } else {
                if ($case.PSObject.Properties.Name -contains "expected_fixture_calls") {
                    foreach ($property in $case.expected_fixture_calls.PSObject.Properties) {
                        $actualProperty = $report.fixture.calls.PSObject.Properties[
                            [string]$property.Name]
                        $actualCount = if ($null -eq $actualProperty) {
                            0
                        } else {
                            [int]$actualProperty.Value
                        }
                        if ($actualCount -lt [int]$property.Value) {
                            $caseErrors.Add(
                                "fixture call '$($property.Name)' count $actualCount is below $($property.Value)")
                        }
                    }
                }
                if ($case.PSObject.Properties.Name -contains "expected_storage_contains") {
                    foreach ($expectation in @($case.expected_storage_contains)) {
                        $storageProperty = $report.fixture.storage.PSObject.Properties[
                            [string]$expectation.key]
                        if ($null -eq $storageProperty) {
                            $caseErrors.Add(
                                "fixture storage key '$($expectation.key)' is missing")
                        } elseif (-not ([string]$storageProperty.Value).Contains(
                                [string]$expectation.contains)) {
                            $caseErrors.Add(
                                "fixture storage key '$($expectation.key)' lacks expected content")
                        }
                    }
                }
                if ($case.PSObject.Properties.Name -contains "expected_monitoring_lease_count" -and
                    [int]$report.fixture.monitoring_lease_count -ne
                        [int]$case.expected_monitoring_lease_count) {
                    $caseErrors.Add(
                        "monitoring lease count $($report.fixture.monitoring_lease_count) does not equal $($case.expected_monitoring_lease_count)")
                }

                $storageHashes = [ordered]@{}
                foreach ($property in $report.fixture.storage.PSObject.Properties) {
                    $storageValue = [string]$property.Value
                    $storageHashes[[string]$property.Name] = [ordered]@{
                        length = $storageValue.Length
                        sha256 = Get-Sha256Text $storageValue
                    }
                }
                $clipboardValue = [string]$report.fixture.clipboard_text
                $fixtureSummary = [ordered]@{
                    clipboard_text_length = $clipboardValue.Length
                    clipboard_text_sha256 = if ($clipboardValue.Length -gt 0) {
                        Get-Sha256Text $clipboardValue
                    } else { $null }
                    monitoring_lease_count =
                        [int]$report.fixture.monitoring_lease_count
                    storage = $storageHashes
                    calls = $report.fixture.calls
                    last_http_url_sha256 =
                        [string]$report.fixture.last_http_url_sha256
                }
            }
        }

        foreach ($caseError in $caseErrors) {
            $failures.Add("$($case.id): $caseError")
        }
        $caseResults.Add([PSCustomObject]@{
            id = [string]$case.id
            plugin_id = [string]$case.plugin_id
            command_id = [string]$case.command_id
            expected_success = [bool]$case.expected_success
            passed = $caseErrors.Count -eq 0
            exit_code = $process.ExitCode
            elapsed_ms = [double]$report.elapsed_ms
            input_text_length = $inputText.Length
            input_text_sha256 = if ($inputText.Length -gt 0) {
                Get-Sha256Text $inputText
            } else { $null }
            output_length = $outputLength
            output_sha256 = $outputSha256
            api_method_calls = $report.api_method_calls
            fixture = $fixtureSummary
            errors = @($caseErrors)
        })
    }
}
finally {
    $resolvedRunRoot = [System.IO.Path]::GetFullPath($runRoot)
    if ($resolvedRunRoot.StartsWith(
            $temporaryBase + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRunRoot)) {
        Remove-Item -LiteralPath $resolvedRunRoot -Recurse -Force
    }
}

$commit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
$dirty = -not [string]::IsNullOrWhiteSpace(
    ((& git -C $PSScriptRoot status --porcelain) -join "`n"))
$hostRelative = $hostExe.Substring($PSScriptRoot.Length)
$hostRelative = $hostRelative.TrimStart(
    [char][System.IO.Path]::DirectorySeparatorChar)
$hostRelative = $hostRelative.Replace(
    [char][System.IO.Path]::DirectorySeparatorChar,
    [char]"/")
$evidence = [ordered]@{
    schema_version = 1
    recorded_at = [DateTimeOffset]::UtcNow.ToString("O")
    source_commit = $commit
    source_dirty = $dirty
    host_executable = $hostRelative
    host_sha256 = (Get-FileHash -LiteralPath $hostExe -Algorithm SHA256).Hash.ToLowerInvariant()
    isolated = $true
    required_plugin_count = [int]$definition.policy.required_plugin_count
    covered_plugin_count = $pluginIds.Count
    required_command_count = [int]$definition.policy.required_command_count
    covered_command_count = $commandIds.Count
    success_case_count = @($cases | Where-Object { [bool]$_.expected_success }).Count
    error_case_count = @($cases | Where-Object { -not [bool]$_.expected_success }).Count
    passed_case_count = @($caseResults | Where-Object passed).Count
    failed_case_count = $failures.Count
    passed = $failures.Count -eq 0 -and $caseResults.Count -eq $cases.Count
    failures = @($failures)
    cases = @($caseResults)
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = "artifacts/quality/low-risk-plugin-commands-$stamp/report.json"
}
$outputFile = Resolve-RepositoryPath $OutputPath
New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($outputFile)) `
    -Force | Out-Null
$evidence | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $outputFile -Encoding UTF8

Write-Host "Plugin command case matrix: $($evidence.passed_case_count)/$($cases.Count) cases passed"
Write-Host "Evidence: $outputFile"
if (-not $evidence.passed) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
