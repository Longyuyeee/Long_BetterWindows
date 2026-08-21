#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)] [string] $ProductAcceptanceGatePath,
    [string] $SubjectExecutable =
        'src/LongBetterWindows.Host/bin/Release/net8.0-windows/LongBetterWindows.Host.exe',
    [Parameter(Mandatory=$true)] [string] $ExpectedSourceCommit,
    [Parameter(Mandatory=$true)]
    [ValidateSet('unsigned','signed')] [string] $ExpectedDistributionChannel,
    [Parameter(Mandatory=$true)]
    [ValidateSet('offline','test_service','production')] [string] $ServiceMode,
    [string] $TestServiceEndpoint,
    [string] $TestServiceCertificateSha256,
    [string] $OutputPath,
    [switch] $PreflightOnly,
    [switch] $AllowDirty,
    [switch] $RequireReleaseEligible
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-evidence-io.ps1')

Add-Type -AssemblyName System.Net.Http
$httpAssemblyPath = [Net.Http.HttpClient].Assembly.Location
$compilerReferences = @(
    $httpAssemblyPath
    [Security.Cryptography.SHA256].Assembly.Location
    [Security.Cryptography.X509Certificates.X509Certificate2].Assembly.Location
    [Net.Security.SslPolicyErrors].Assembly.Location
) | Sort-Object -Unique
Add-Type -TypeDefinition @'
using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static class LongAuthenticodeVerifier
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public string UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr window,
        [In] ref Guid action,
        IntPtr trustData);

    public static HttpClientHandler CreatePinnedHandler(string expectedSha256)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback =
            (request, certificate, chain, errors) =>
            {
                if (certificate == null)
                    return false;
                using (var algorithm = SHA256.Create())
                {
                    var actual = BitConverter.ToString(
                        algorithm.ComputeHash(certificate.RawData))
                        .Replace("-", "")
                        .ToLowerInvariant();
                    return string.Equals(
                        actual,
                        expectedSha256,
                        StringComparison.Ordinal);
                }
            };
        return handler;
    }

    public static string GetStatus(string path)
    {
        var file = new WinTrustFileInfo
        {
            StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
            FilePath = path,
        };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(file));
        var dataPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = filePointer,
                StateAction = 0,
                ProviderFlags = 0,
                UiContext = 0,
            };
            dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPointer, false);
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(IntPtr.Zero, ref action, dataPointer);
            if (result == 0)
                return "Valid";
            if (result == unchecked((int)0x800B0100) ||
                result == unchecked((int)0x800B0003) ||
                result == unchecked((int)0x800B0001))
                return "NotSigned";
            return "Invalid";
        }
        finally
        {
            if (dataPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(dataPointer);
            Marshal.FreeHGlobal(filePointer);
        }
    }
}
'@ -ReferencedAssemblies $compilerReferences

function Resolve-RepositoryPath([string] $PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PathValue))
}

function Read-JsonFile([string] $Path, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }
    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "$Label is not valid JSON: $Path"
    }
}

function Get-ByteSha256([byte[]] $Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Assert-FinalProductAcceptance(
    $Document,
    [string] $ExpectedCommit,
    [bool] $ExpectedDirty,
    [string] $GatePath,
    [string] $HostPath,
    [string] $HostHash) {
    $identityInvalid = (
        [int]$Document.schema_version -ne 3 -or
        [string]$Document.classification -ne 'automated_final_product_acceptance' -or
        [string]$Document.source_commit -ne $ExpectedCommit -or
        [bool]$Document.source_dirty -ne $ExpectedDirty -or
        [int]$Document.plugin_count -ne 25 -or
        [int]$Document.command_count -ne 42 -or
        [int]$Document.automated_gate_count -ne 94 -or
        [int]$Document.failed_gate_count -ne 0 -or
        [int]$Document.not_run_gate_count -ne 0 -or
        -not [bool]$Document.contract_valid)
    if ($identityInvalid) {
        throw 'Automated final product-acceptance contract is incomplete.'
    }
    $classified = [int]$Document.passed_gate_count +
        [int]$Document.failed_gate_count +
        [int]$Document.environment_blocked_gate_count +
        [int]$Document.not_run_gate_count +
        [int]$Document.not_applicable_gate_count
    if ($classified -ne 94) {
        throw 'Automated final product-acceptance gate counts are inconsistent.'
    }
    if ([string]$Document.release_host.sha256 -ne $HostHash) {
        throw 'Automated final product-acceptance host identity is invalid.'
    }
    $reportedHost = [IO.Path]::GetFullPath([string]$Document.release_host.path)
    if (-not [string]::Equals(
        $reportedHost,
        $HostPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Automated final product-acceptance host path does not match the subject executable.'
    }
    $closureFile = [string]$Document.final_closure.file
    if ($closureFile -notmatch '^[^/]+\.sources/final-closure\.json$' -or
        [string]$Document.final_closure.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Automated final product-acceptance closure identity is incomplete.'
    }
    $closurePath = Join-Path (Split-Path -Parent $GatePath) (
        $closureFile.Replace('/', [IO.Path]::DirectorySeparatorChar))
    $closureHashMatches = (
        (Test-Path -LiteralPath $closurePath -PathType Leaf) -and
        (Get-FileHash -LiteralPath $closurePath -Algorithm SHA256).
            Hash.ToLowerInvariant() -eq [string]$Document.final_closure.sha256)
    if (-not $closureHashMatches) {
        throw 'Automated final product-acceptance closure hash mismatch.'
    }
    $closure = Read-JsonFile $closurePath 'Portable final closure'
    if ([int]$closure.schema_version -ne 2 -or
        [string]$closure.classification -ne 'final_closure' -or
        [string]$closure.source_commit -ne $ExpectedCommit -or
        [bool]$closure.source_dirty -ne $ExpectedDirty) {
        throw 'Portable final closure identity is invalid.'
    }
}

function Invoke-TestServiceProbe(
    [string] $Endpoint,
    [string] $CertificateSha256,
    [string] $ExpectedCommit) {
    $baseUri = $null
    $validEndpoint = (
        [Uri]::TryCreate($Endpoint, [UriKind]::Absolute, [ref]$baseUri) -and
        $baseUri.Scheme -eq 'https' -and
        $baseUri.IsLoopback -and
        [string]::IsNullOrWhiteSpace($baseUri.Query) -and
        [string]::IsNullOrWhiteSpace($baseUri.Fragment))
    if (-not $validEndpoint) {
        throw 'Test service endpoint must be an absolute loopback HTTPS URI.'
    }
    if ($CertificateSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'Test service certificate SHA-256 pin is required.'
    }
    $handler = [LongAuthenticodeVerifier]::CreatePinnedHandler(
        $CertificateSha256)
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $client.MaxResponseContentBufferSize = 16MB
    try {
        $registryUri = [Uri]::new($baseUri, 'registry.json')
        $registryBytes = $client.GetByteArrayAsync($registryUri).
            GetAwaiter().GetResult()
        try {
            $registry = [Text.Encoding]::UTF8.GetString($registryBytes) |
                ConvertFrom-Json
        }
        catch {
            throw 'Test service Registry is not valid JSON.'
        }
        $requiredCapabilities = @(
            'registry_fetch',
            'package_fetch',
            'rollback',
            'offline_fallback')
        $capabilities = @($registry.capabilities | ForEach-Object {
            [string]$_
        } | Sort-Object -Unique)
        $registryInvalid = (
            [int]$registry.schema_version -ne 1 -or
            [string]$registry.classification -ne 'long_marketplace_test_service_registry' -or
            [string]$registry.source_commit -ne $ExpectedCommit -or
            $capabilities.Count -ne $requiredCapabilities.Count -or
            @(Compare-Object (
                $requiredCapabilities | Sort-Object) $capabilities).Count -ne 0 -or
            [string]$registry.package.path -notmatch '^[A-Za-z0-9._-]+$' -or
            [string]$registry.package.sha256 -notmatch '^[0-9a-f]{64}$' -or
            [long]$registry.package.bytes -le 0)
        if ($registryInvalid) {
            throw 'Test service Registry contract is incomplete.'
        }
        $packageUri = [Uri]::new($baseUri, [string]$registry.package.path)
        if ($packageUri.Scheme -ne $baseUri.Scheme -or
            $packageUri.Host -ne $baseUri.Host -or
            $packageUri.Port -ne $baseUri.Port) {
            throw 'Test service package must remain on the same loopback origin.'
        }
        $packageBytes = $client.GetByteArrayAsync($packageUri).
            GetAwaiter().GetResult()
        $packageHash = Get-ByteSha256 $packageBytes
        if ($packageBytes.LongLength -ne [long]$registry.package.bytes -or
            $packageHash -ne [string]$registry.package.sha256) {
            throw 'Test service package bytes do not match the Registry.'
        }
        return [ordered]@{
            endpoint = $baseUri.AbsoluteUri
            certificate_sha256 = $CertificateSha256
            registry_sha256 = Get-ByteSha256 $registryBytes
            package_file = [string]$registry.package.path
            package_sha256 = $packageHash
            package_bytes = $packageBytes.LongLength
            capabilities = $requiredCapabilities
        }
    }
    finally {
        $client.Dispose()
    }
}

$head = (& git -C $PSScriptRoot rev-parse HEAD).Trim().ToLowerInvariant()
$expectedCommit = $ExpectedSourceCommit.Trim().ToLowerInvariant()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$' -or $expectedCommit -ne $head) {
    throw 'ExpectedSourceCommit must exactly match the current 40-character HEAD.'
}
$trackedStatus = @(& git -C $PSScriptRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the tracked worktree.'
}
$sourceDirty = $trackedStatus.Count -ne 0
if ($sourceDirty -and -not $AllowDirty) {
    throw 'Release channel policy requires a clean tracked worktree.'
}
if ($PreflightOnly -and -not [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw 'PreflightOnly does not accept OutputPath and never writes a report.'
}
if (-not $PreflightOnly -and [string]::IsNullOrWhiteSpace($OutputPath)) {
    throw 'OutputPath is required unless PreflightOnly is specified.'
}
if ($ServiceMode -ne 'test_service' -and
    (-not [string]::IsNullOrWhiteSpace($TestServiceEndpoint) -or
     -not [string]::IsNullOrWhiteSpace($TestServiceCertificateSha256))) {
    throw 'Test service endpoint and certificate pin are only valid for test_service mode.'
}
if ($ServiceMode -eq 'test_service' -and
    ([string]::IsNullOrWhiteSpace($TestServiceEndpoint) -or
     [string]::IsNullOrWhiteSpace($TestServiceCertificateSha256))) {
    throw 'Test service endpoint and certificate pin are required for test_service mode.'
}

$productPath = Resolve-RepositoryPath $ProductAcceptanceGatePath
$productJson = Get-Content -LiteralPath $productPath -Raw -Encoding UTF8
$product = Read-JsonFile $productPath 'Automated final product acceptance'
$hostPath = Resolve-RepositoryPath $SubjectExecutable
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw "Subject executable was not found: $hostPath"
}
$hostHash = (Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).
    Hash.ToLowerInvariant()
Assert-FinalProductAcceptance (
    $product) $expectedCommit $sourceDirty $productPath $hostPath $hostHash

$signatureStatus = [LongAuthenticodeVerifier]::GetStatus($hostPath)
if ($ExpectedDistributionChannel -eq 'unsigned') {
    if ($signatureStatus -ne 'NotSigned') {
        throw 'Unsigned channel requires an executable with no Authenticode signature.'
    }
}
elseif ($signatureStatus -ne 'Valid') {
    throw 'Signed channel requires a valid Authenticode signature.'
}

$serviceProbe = $null
$policyBlockers = @()
switch ($ServiceMode) {
    'offline' {
        $serviceStatus = 'disabled_by_policy'
    }
    'test_service' {
        $serviceProbe = Invoke-TestServiceProbe (
            $TestServiceEndpoint) $TestServiceCertificateSha256 $expectedCommit
        $serviceStatus = 'verified_test_service'
    }
    'production' {
        $serviceStatus = 'blocked_environment'
        $policyBlockers += [ordered]@{
            id = 'production-marketplace'
            reason = 'Production Registry/CDN credentials are external channel configuration.'
        }
    }
}
$productBlockers = @($product.environment_blockers | ForEach-Object {
    [ordered]@{
        id = [string]$_.gate_id
        reason = [string]$_.reason
    }
})
$blockers = @($productBlockers) + @($policyBlockers)
$releaseEligible = (
    [bool]$product.release_eligible -and
    $policyBlockers.Count -eq 0 -and
    -not $sourceDirty)
$status = if ($releaseEligible) {
    'passed'
} elseif ($blockers.Count -gt 0) {
    'blocked_environment'
} else {
    'not_eligible'
}

$outputName = if ($PreflightOnly) {
    'preflight'
} else {
    [IO.Path]::GetFileNameWithoutExtension($OutputPath)
}
$sourceDirectoryName = "$outputName.sources"
$summary = [ordered]@{
    '$schema' = 'https://long-assistant.local/schemas/release-channel-policy-report.schema.json'
    schema_version = 1
    generated_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
    classification = 'automated_release_channel_policy'
    preflight_only = [bool]$PreflightOnly
    source_commit = $expectedCommit
    source_dirty = $sourceDirty
    policy_status = $status
    distribution_channel = $ExpectedDistributionChannel
    authenticode_status = $signatureStatus
    service_mode = $ServiceMode
    service_status = $serviceStatus
    product_acceptance = [ordered]@{
        file = "$sourceDirectoryName/final-product-acceptance.json"
        sha256 = (Get-FileHash -LiteralPath $productPath -Algorithm SHA256).Hash.ToLowerInvariant()
        release_eligible = [bool]$product.release_eligible
    }
    release_host = [ordered]@{
        path = $hostPath
        sha256 = $hostHash
    }
    test_service = $serviceProbe
    environment_blockers = $blockers
    release_eligible = $releaseEligible
}

if ($PreflightOnly) {
    $summary | ConvertTo-Json -Depth 10
}
else {
    $resolvedOutput = Resolve-RepositoryPath $OutputPath
    if (Test-Path -LiteralPath $resolvedOutput) {
        throw "Release channel policy report already exists: $resolvedOutput"
    }
    $outputParent = Split-Path -Parent $resolvedOutput
    [IO.Directory]::CreateDirectory($outputParent) | Out-Null
    $sourceDirectoryName =
        [IO.Path]::GetFileNameWithoutExtension($resolvedOutput) + '.sources'
    $sourceDirectory = Join-Path $outputParent $sourceDirectoryName
    if (Test-Path -LiteralPath $sourceDirectory) {
        throw "Release channel policy source directory already exists: $sourceDirectory"
    }
    $summary.product_acceptance.file =
        "$sourceDirectoryName/final-product-acceptance.json"
    $stage = Join-Path $outputParent (
        '.release-channel-policy-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $sourceCommitted = $false
    try {
        Copy-Item -LiteralPath $productPath -Destination (
            Join-Path $stage 'final-product-acceptance.json')
        [IO.Directory]::Move($stage, $sourceDirectory)
        $sourceCommitted = $true
        Write-NewJsonFileAtomically (
            $summary) $resolvedOutput 10 'Release channel policy report'
    }
    catch {
        if ($sourceCommitted -and (Test-Path -LiteralPath $sourceDirectory)) {
            Remove-Item -LiteralPath $sourceDirectory -Recurse -Force
        }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
    $summary | ConvertTo-Json -Depth 10
}
if ($RequireReleaseEligible -and -not $releaseEligible) {
    exit 2
}
