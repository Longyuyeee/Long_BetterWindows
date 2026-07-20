#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $OutputDirectory,
    [ValidateRange(5,60)] [int] $TimeoutSeconds = 20,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Marketplace transaction output directory already exists: $outputRoot"
}
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$transactionRoot = Join-Path $outputRoot 'transaction-temp'
$packagesRoot = Join-Path $transactionRoot 'packages'
$isolatedPlugins = Join-Path $transactionRoot 'plugins'
[IO.Directory]::CreateDirectory($packagesRoot) | Out-Null
[IO.Directory]::CreateDirectory($isolatedPlugins) | Out-Null

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet CLI was not found.' }
    $dotnet = $dotnetCommand.Source
}
$project = Join-Path $repoRoot 'src\LongBetterWindows.Host\LongBetterWindows.Host.csproj'
$executable = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\LongBetterWindows.Host.exe'
$releasePlugins = Join-Path $repoRoot 'src\LongBetterWindows.Host\bin\Release\net8.0-windows\Plugins'
if (-not $NoBuild) {
    & $dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Marketplace transaction Release build failed.' }
}
if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable was not found: $executable" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public static class LongTransactionWindows {
    delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    public static IntPtr[] TopLevelWindows(int processId) {
        var windows = new List<IntPtr>();
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }
    public static string ExportRsaPublicKeyPem(RSA rsa) {
        var parameters = rsa.ExportParameters(false);
        var body = new List<byte>();
        body.AddRange(DerInteger(parameters.Modulus));
        body.AddRange(DerInteger(parameters.Exponent));
        var der = DerValue(0x30, body.ToArray());
        var base64 = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks)
            .Replace("\r\n", "\n");
        return "-----BEGIN RSA PUBLIC KEY-----\n" + base64 +
            "\n-----END RSA PUBLIC KEY-----";
    }
    static byte[] DerInteger(byte[] value) {
        var start = 0;
        while (start < value.Length - 1 && value[start] == 0) start++;
        var content = new List<byte>();
        if ((value[start] & 0x80) != 0) content.Add(0);
        for (var index = start; index < value.Length; index++) content.Add(value[index]);
        return DerValue(0x02, content.ToArray());
    }
    static byte[] DerValue(byte tag, byte[] content) {
        var result = new List<byte> { tag };
        result.AddRange(DerLength(content.Length));
        result.AddRange(content);
        return result.ToArray();
    }
    static byte[] DerLength(int length) {
        if (length < 128) return new[] { (byte)length };
        var bytes = new List<byte>();
        for (var value = length; value > 0; value >>= 8)
            bytes.Insert(0, (byte)(value & 0xff));
        bytes.Insert(0, (byte)(0x80 | bytes.Count));
        return bytes.ToArray();
    }
}
'@

function Write-Stage([string] $message) {
    $line = "[$([DateTimeOffset]::Now.ToString('O'))] $message"
    Write-Output "[marketplace-transaction] $message"
    Add-Content -LiteralPath (Join-Path $outputRoot 'marketplace-transaction.log') `
        -Value $line -Encoding UTF8
}

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

function Find-DescendantByAutomationId(
    [Windows.Automation.AutomationElement] $root,
    [string] $automationId) {
    $elements = $root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements.Item($index)
        if ($element.Current.AutomationId -eq $automationId) { return $element }
    }
    return $null
}

function Find-ProcessElementByAutomationId([int] $processId, [string] $automationId) {
    foreach ($window in [LongTransactionWindows]::TopLevelWindows($processId)) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($window)
        if ($root.Current.AutomationId -eq $automationId) { return $root }
        $match = Find-DescendantByAutomationId $root $automationId
        if ($null -ne $match) { return $match }
    }
    return $null
}

function Find-ProcessElementByName([int] $processId, [string] $name) {
    foreach ($window in [LongTransactionWindows]::TopLevelWindows($processId)) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($window)
        if ($root.Current.Name -eq $name) { return $root }
        $elements = $root.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.Condition]::TrueCondition)
        for ($index = 0; $index -lt $elements.Count; $index++) {
            $element = $elements.Item($index)
            if ($element.Current.Name -eq $name) { return $element }
        }
    }
    return $null
}

function Find-ProcessSelectableElementByName([int] $processId, [string] $name) {
    foreach ($window in [LongTransactionWindows]::TopLevelWindows($processId)) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($window)
        $elements = $root.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.Condition]::TrueCondition)
        for ($index = 0; $index -lt $elements.Count; $index++) {
            $element = $elements.Item($index)
            if ($element.Current.Name -ne $name) { continue }
            $selection = $null
            if ($element.TryGetCurrentPattern(
                [Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
                return $element
            }
        }
    }
    return $null
}

function Invoke-Element([Windows.Automation.AutomationElement] $element, [string] $failure) {
    if ($null -eq $element -or -not $element.Current.IsEnabled) { throw $failure }
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
        [Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { throw $failure }
    ([Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Select-Version([int] $processId, [string] $version) {
    $combo = Wait-Until {
        Find-ProcessElementByAutomationId $processId 'Long.Marketplace.Version'
    } 'Marketplace version selector was not discoverable.'
    $expand = $null
    if (-not $combo.TryGetCurrentPattern(
        [Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$expand)) {
        throw 'Marketplace version selector did not support ExpandCollapsePattern.'
    }
    ([Windows.Automation.ExpandCollapsePattern]$expand).Expand()
    $item = Wait-Until {
        Find-ProcessSelectableElementByName $processId $version
    } "Marketplace version $version was not discoverable."
    $selection = $null
    if (-not $item.TryGetCurrentPattern(
        [Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
        throw "Marketplace version $version did not support SelectionItemPattern."
    }
    ([Windows.Automation.SelectionItemPattern]$selection).Select()
    ([Windows.Automation.ExpandCollapsePattern]$expand).Collapse()
    Start-Sleep -Milliseconds 250
}

function Get-DirectoryFingerprint([string] $path) {
    $records = @()
    if (Test-Path -LiteralPath $path) {
        $root = [IO.Path]::GetFullPath($path).TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse | Sort-Object FullName) {
            $relative = $file.FullName.Substring($root.Length).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $records += "$relative|$($file.Length)|$hash"
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
        return ConvertTo-HexString ($sha.ComputeHash($bytes))
    }
    finally { $sha.Dispose() }
}

function ConvertTo-HexString([byte[]] $bytes) {
    return [BitConverter]::ToString($bytes).Replace('-', '')
}

function Write-ZipText($archive, [string] $name, [string] $content) {
    $entry = $archive.CreateEntry($name)
    $writer = [IO.StreamWriter]::new(
        $entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write($content) } finally { $writer.Dispose() }
}

$pluginId = 'dev.long.ui-transaction'
$pluginDirectory = Join-Path $isolatedPlugins 'dev-long-ui-transaction'
$rsa = [Security.Cryptography.RSA]::Create(2048)
$publicKey = [LongTransactionWindows]::ExportRsaPublicKeyPem($rsa)
$fingerprint = ''

function New-SignedPackage([string] $version) {
    $path = Join-Path $packagesRoot "transaction-$version.lpak"
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifest = [ordered]@{
            id = $pluginId
            version = $version
            name = 'Long Transaction Fixture'
            author = 'Long Quality'
            runtime = 'webview'
            entry_point = 'index.html'
            capabilities = @('storage.local')
            min_host_version = '0.5.0'
            min_api_version = '1.0.0'
            min_ui_kit_version = '1.0.0'
            lifecycle = [ordered]@{ start_with_host = $false; default_presentation = 'embedded'; close_behavior = 'stop' }
        } | ConvertTo-Json -Depth 8
        Write-ZipText $archive 'manifest.json' $manifest
        Write-ZipText $archive 'index.html' "<!doctype html><title>Long transaction $version</title><main>v$version</main>"
    }
    finally { $archive.Dispose() }
    $bytes = [IO.File]::ReadAllBytes($path)
    $packageHasher = [Security.Cryptography.SHA256]::Create()
    try { $hash = $packageHasher.ComputeHash($bytes) }
    finally { $packageHasher.Dispose() }
    return [ordered]@{
        Version = $version
        PackageUri = ([Uri]::new($path)).AbsoluteUri
        Sha256 = ConvertTo-HexString $hash
        Signature = [Convert]::ToBase64String($rsa.SignHash(
            $hash,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1))
        PublisherPublicKeyPem = $publicKey
        PublisherKeyId = 'long-ui-transaction-root'
        PublishedAt = [DateTimeOffset]::UtcNow.ToString('O')
        ReleaseNotes = "Isolated transaction fixture $version"
        Capabilities = @('storage.local')
        MinHostVersion = '0.5.0'
        MinApiVersion = '1.0.0'
        MinUiKitVersion = '1.0.0'
    }
}

$v1 = New-SignedPackage '1.0.0'
$v2 = New-SignedPackage '2.0.0'
$v3 = New-SignedPackage '3.0.0'
[IO.File]::AppendAllText(([Uri]$v3.PackageUri).LocalPath, 'tampered-after-signing')
$v4 = New-SignedPackage '4.0.0'
$forgedRsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $forgedBytes = [IO.File]::ReadAllBytes(([Uri]$v4.PackageUri).LocalPath)
    $forgedHasher = [Security.Cryptography.SHA256]::Create()
    try { $forgedHash = $forgedHasher.ComputeHash($forgedBytes) }
    finally { $forgedHasher.Dispose() }
    $v4.Signature = [Convert]::ToBase64String($forgedRsa.SignHash(
        $forgedHash,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1))
}
finally { $forgedRsa.Dispose() }
$trustPath = Join-Path $transactionRoot 'trusted-publishers.json'
$catalogPath = Join-Path $transactionRoot 'registry.json'
([ordered]@{
    SchemaVersion = 1
    Publishers = @([ordered]@{
        KeyId = 'long-ui-transaction-root'
        Publisher = 'Long Quality'
        Algorithm = 'RSA-SHA256'
        PublicKeyPem = $publicKey
        Sha256Fingerprint = $fingerprint
    })
} | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $trustPath -Encoding UTF8
([ordered]@{
    SchemaVersion = 1
    Source = 'remote_registry'
    GeneratedAt = [DateTimeOffset]::UtcNow.ToString('O')
    Entries = @([ordered]@{
        Id = $pluginId
        Name = 'Long Transaction Fixture'
        Summary = 'Signed isolated install transaction'
        Description = 'Quality-only signed Web plugin used in an isolated directory.'
        Publisher = 'Long Quality'
        Category = 'Quality'
        Tags = @('transaction', 'signed', 'isolated')
        Versions = @($v4, $v3, $v2, $v1)
    })
} | ConvertTo-Json -Depth 12) | Set-Content -LiteralPath $catalogPath -Encoding UTF8

$releaseBefore = Get-DirectoryFingerprint $releasePlugins
$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'isolated_marketplace_failure_recovery_ui_transaction'
    plugin_id = $pluginId
    isolation_root_removed = $false
    release_plugins_fingerprint_before = $releaseBefore
    release_plugins_fingerprint_after = $null
    release_plugins_unchanged = $false
    signed_trust_confirmed = $false
    install_v1 = $false
    rejected_hash_mismatch = $false
    rejected_forged_signature = $false
    old_version_preserved_after_rejections = $false
    upgrade_v2 = $false
    rollback_v1 = $false
    uninstall = $false
    startup_recovered_interrupted_upgrade = $false
    passed = $false
    error = $null
    failed_stage = $null
}
$hostProcess = $null
$stage = 'startup'

function Read-IsolatedVersion {
    $manifestPath = Join-Path $pluginDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $null }
    try { return (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).version }
    catch { return $null }
}

function Confirm-SelectedPackage([string] $expectedVersion) {
    $install = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.Install'
        if ($null -ne $element -and $element.Current.IsEnabled) { $element }
    } "Install action was unavailable for v$expectedVersion."
    Invoke-Element $install "Install action could not be invoked for v$expectedVersion."
    $trust = Wait-Until {
        Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmTrust'
    } "Trust confirmation did not appear for v$expectedVersion."
    if ($trust.Current.ItemStatus -ne 'PublisherSigned') {
        throw "Publisher signature was not confirmed for v$expectedVersion (ItemStatus=$($trust.Current.ItemStatus))."
    }
    $report.signed_trust_confirmed = $true
    $confirm = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmAction'
        if ($null -ne $element -and $element.Current.IsEnabled) { $element }
    } "Confirmation action was unavailable for v$expectedVersion."
    Invoke-Element $confirm "Confirmation action could not be invoked for v$expectedVersion."
    Wait-Until {
        (Read-IsolatedVersion) -eq $expectedVersion
    } "Isolated plugin did not reach version $expectedVersion." | Out-Null
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmTitle')
    } "Confirmation overlay remained open after installing v$expectedVersion." | Out-Null
}

function Confirm-RejectedPackage([string] $expectedVersion, [string] $expectedStatus) {
    Select-Version $hostProcess.Id $expectedVersion
    $install = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.Install'
        if ($null -ne $element -and $element.Current.IsEnabled) { $element }
    } "Install action was unavailable for rejected v$expectedVersion."
    Invoke-Element $install "Rejected v$expectedVersion could not be submitted for validation."
    $trust = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmTrust'
        if ($null -ne $element -and $element.Current.ItemStatus -eq $expectedStatus) { $element }
    } "Rejected v$expectedVersion did not expose $expectedStatus."
    $confirm = Find-ProcessElementByAutomationId `
        $hostProcess.Id 'Long.Marketplace.ConfirmAction'
    if ($null -eq $confirm -or $confirm.Current.IsEnabled) {
        throw "Rejected v$expectedVersion left its confirmation action enabled."
    }
    if ((Read-IsolatedVersion) -ne '1.0.0') {
        throw "Rejected v$expectedVersion changed the installed v1 fixture."
    }
    $cancel = Find-ProcessElementByAutomationId `
        $hostProcess.Id 'Long.Marketplace.ConfirmCancel'
    Invoke-Element $cancel "Rejected v$expectedVersion overlay could not be closed."
    Wait-Until {
        $null -eq (Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmTitle')
    } "Rejected v$expectedVersion overlay remained open." | Out-Null
}

try {
    Write-Stage 'Starting isolated Marketplace host.'
    $arguments = @(
        '--theme', 'dark',
        '--plugins-dir', $isolatedPlugins,
        '--quality-open-market',
        '--quality-market-catalog', $catalogPath,
        '--quality-market-trust-store', $trustPath
    )
    $hostProcess = Start-Process -FilePath $executable -ArgumentList $arguments `
        -WorkingDirectory $outputRoot -PassThru
    Start-Sleep -Seconds 4
    Wait-Until {
        Find-ProcessElementByAutomationId $hostProcess.Id 'Long.Marketplace.DetailName'
    } 'Isolated Marketplace fixture detail did not appear.' | Out-Null

    $stage = 'install_v1'
    Write-Stage 'Selecting and installing signed v1.0.0.'
    Select-Version $hostProcess.Id '1.0.0'
    Confirm-SelectedPackage '1.0.0'
    $report.install_v1 = $true

    $stage = 'rejected_hash_mismatch'
    Write-Stage 'Rejecting a package changed after its catalog hash was signed.'
    Confirm-RejectedPackage '3.0.0' 'HashRejected'
    $report.rejected_hash_mismatch = $true

    $stage = 'rejected_forged_signature'
    Write-Stage 'Rejecting a package signed by an untrusted private key.'
    Confirm-RejectedPackage '4.0.0' 'SignatureRejected'
    $report.rejected_forged_signature = $true
    $report.old_version_preserved_after_rejections =
        (Read-IsolatedVersion) -eq '1.0.0'
    if (-not $report.old_version_preserved_after_rejections) {
        throw 'Installed v1 was not preserved after rejected updates.'
    }

    $stage = 'upgrade_v2'
    Write-Stage 'Selecting and upgrading to signed v2.0.0.'
    Select-Version $hostProcess.Id '2.0.0'
    Confirm-SelectedPackage '2.0.0'
    $report.upgrade_v2 = $true

    $stage = 'rollback_v1'
    Write-Stage 'Selecting signed v1.0.0 as a version rollback.'
    Select-Version $hostProcess.Id '1.0.0'
    Confirm-SelectedPackage '1.0.0'
    $report.rollback_v1 = $true

    $stage = 'uninstall'
    Write-Stage 'Uninstalling the fixture from the isolated plugin directory.'
    $uninstall = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.Uninstall'
        if ($null -ne $element -and $element.Current.IsEnabled) { $element }
    } 'Uninstall action was unavailable after rollback.'
    Invoke-Element $uninstall 'Uninstall action could not be invoked.'
    $confirmUninstall = Wait-Until {
        $element = Find-ProcessElementByAutomationId `
            $hostProcess.Id 'Long.Marketplace.ConfirmAction'
        if ($null -ne $element -and $element.Current.IsEnabled) { $element }
    } 'Uninstall confirmation action was unavailable.'
    Invoke-Element $confirmUninstall 'Uninstall confirmation could not be invoked.'
    Wait-Until { -not (Test-Path -LiteralPath $pluginDirectory) } `
        'Isolated plugin directory remained after uninstall.' | Out-Null
    $report.uninstall = $true

    $stage = 'startup_recovered_interrupted_upgrade'
    Write-Stage 'Restarting from an interrupted upgrade journal and restoring v1.'
    Stop-Process -Id $hostProcess.Id -Force
    $hostProcess.WaitForExit(5000) | Out-Null
    $hostProcess = $null
    [IO.Compression.ZipFile]::ExtractToDirectory(
        ([Uri]$v2.PackageUri).LocalPath, $pluginDirectory)
    $recoveryTransaction = Join-Path $transactionRoot `
        '.long-transaction-quality-interrupted-upgrade'
    $recoveryBackup = Join-Path $recoveryTransaction 'backup'
    [IO.Directory]::CreateDirectory($recoveryTransaction) | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory(
        ([Uri]$v1.PackageUri).LocalPath, $recoveryBackup)
    ([ordered]@{ PluginId = $pluginId; Phase = 1 } | ConvertTo-Json) |
        Set-Content -LiteralPath (Join-Path $recoveryTransaction 'journal.json') -Encoding UTF8
    $hostProcess = Start-Process -FilePath $executable -ArgumentList $arguments `
        -WorkingDirectory $outputRoot -PassThru
    Wait-Until {
        (Read-IsolatedVersion) -eq '1.0.0' -and
        -not (Test-Path -LiteralPath $recoveryTransaction)
    } 'Host startup did not restore the interrupted upgrade to v1.' | Out-Null
    Wait-Until {
        Find-ProcessElementByAutomationId $hostProcess.Id 'Long.Marketplace.DetailName'
    } 'Marketplace did not recover after interrupted transaction startup.' | Out-Null
    $report.startup_recovered_interrupted_upgrade = $true
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
    $report.release_plugins_fingerprint_after = Get-DirectoryFingerprint $releasePlugins
    $report.release_plugins_unchanged =
        $report.release_plugins_fingerprint_before -eq $report.release_plugins_fingerprint_after
    if (-not $report.release_plugins_unchanged) {
        $report.passed = $false
        if ($null -eq $report.error) { $report.error = 'Release plugin directory changed.' }
    }
    $rsa.Dispose()
    if (Test-Path -LiteralPath $transactionRoot) {
        $resolvedTransaction = (Resolve-Path -LiteralPath $transactionRoot).Path
        if (-not $resolvedTransaction.StartsWith(
            $outputRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove transaction path outside output: $resolvedTransaction"
        }
        Remove-Item -LiteralPath $resolvedTransaction -Recurse -Force
    }
    $report.isolation_root_removed = -not (Test-Path -LiteralPath $transactionRoot)
    if (-not $report.isolation_root_removed) {
        $report.passed = $false
        if ($null -eq $report.error) { $report.error = 'Transaction isolation root was not removed.' }
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content `
        -LiteralPath (Join-Path $outputRoot 'marketplace-transaction.json') -Encoding UTF8
}

if (-not $report.passed) {
    throw "Isolated Marketplace transaction failed at $($report.failed_stage): $($report.error)"
}
Write-Output 'Isolated Marketplace transaction passed.'
Write-Output "Report: $(Join-Path $outputRoot 'marketplace-transaction.json')"
