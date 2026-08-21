using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Json.Schema;

namespace LongBetterWindows.Tests;

public sealed class ExternalReleaseGateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-a007-tests",
        Guid.NewGuid().ToString("N"));
    private static readonly Lazy<JsonSchema> ReportSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(
            FindRoot(),
            "schemas",
            "release-channel-policy-report.schema.json"))));

    [Fact]
    public void Gate_HasNoActiveHumanOrProductionCredentialContract()
    {
        var source = Read("verify-external-release-gate.ps1");

        Assert.Contains("automated_release_channel_policy", source);
        Assert.Contains("LongAuthenticodeVerifier", source);
        Assert.Contains("WinVerifyTrust", source);
        Assert.Contains("TestServiceEndpoint", source);
        Assert.Contains("IsLoopback", source);
        Assert.Contains("offline_fallback", source);
        Assert.DoesNotContain("Reviewer", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approval", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Receipt", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MarketplaceRehearsalPath", source);
        Assert.DoesNotContain("ExternalEcosystemDeferralPath", source);
        Assert.DoesNotContain("approved_final_product_acceptance", source);
    }

    [Fact]
    public async Task Gate_AcceptsRealUnsignedOfflineHostAndPackagesSource()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);
        var output = Path.Combine(_root, "offline-policy.json");

        var result = await RunGateAsync(fixture, "offline", output: output);

        Assert.True(
            result.ExitCode == 0,
            $"Exit {result.ExitCode}\nSTDOUT:\n{result.Output}\nSTDERR:\n{result.Error}");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        var root = report.RootElement;
        Assert.Equal("unsigned", root.GetProperty("distribution_channel").GetString());
        Assert.Equal("NotSigned", root.GetProperty("authenticode_status").GetString());
        Assert.Equal("offline", root.GetProperty("service_mode").GetString());
        Assert.Equal("disabled_by_policy", root.GetProperty("service_status").GetString());
        Assert.Equal("blocked_environment", root.GetProperty("policy_status").GetString());
        Assert.False(root.GetProperty("release_eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("test_service").ValueKind);
        var source = root.GetProperty("product_acceptance");
        var sourcePath = Path.Combine(
            _root,
            source.GetProperty("file").GetString()!
                .Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(
            source.GetProperty("sha256").GetString(),
            Hash(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task Gate_ProbesRealLoopbackRegistryAndPackage()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);
        var package = Encoding.UTF8.GetBytes("real loopback package payload");
        await using var server = new LoopbackMarketplaceServer(
            await GitAsync("rev-parse", "HEAD"),
            package);
        var output = Path.Combine(_root, "test-service-policy.json");

        var result = await RunGateAsync(
            fixture,
            "test_service",
            output,
            server.Endpoint,
            server.CertificateSha256);

        Assert.True(
            result.ExitCode == 0,
            $"Exit {result.ExitCode}\nSTDOUT:\n{result.Output}\nSTDERR:\n{result.Error}");
        await server.Completion;
        Assert.Equal(
            new[] { "/registry.json", "/package.bin" },
            server.RequestPaths);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        var probe = report.RootElement.GetProperty("test_service");
        Assert.Equal(Hash(package), probe.GetProperty("package_sha256").GetString());
        Assert.Equal(
            server.CertificateSha256,
            probe.GetProperty("certificate_sha256").GetString());
        Assert.Equal(package.Length, probe.GetProperty("package_bytes").GetInt64());
        Assert.Equal(
            "verified_test_service",
            report.RootElement.GetProperty("service_status").GetString());
    }

    [Fact]
    public async Task Gate_RejectsNonLoopbackTestServiceBeforeNetworkAccess()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);

        var result = await RunGateAsync(
            fixture,
            "test_service",
            output: Path.Combine(_root, "external.json"),
            endpoint: "https://example.com/",
            certificateSha256: new string('a', 64));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("loopback", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gate_RejectsTamperedPackageReturnedByRealService()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);
        await using var server = new LoopbackMarketplaceServer(
            await GitAsync("rev-parse", "HEAD"),
            Encoding.UTF8.GetBytes("expected package"),
            tamperPackageResponse: true);

        var result = await RunGateAsync(
            fixture,
            "test_service",
            Path.Combine(_root, "tampered-service.json"),
            server.Endpoint,
            server.CertificateSha256);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("package bytes", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_root, "tampered-service.json")));
    }

    [Fact]
    public async Task Gate_RejectsSignedChannelForActualUnsignedHost()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);

        var result = await RunGateAsync(
            fixture,
            "offline",
            Path.Combine(_root, "signed.json"),
            distributionChannel: "signed");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("valid Authenticode", result.Error);
    }

    [Fact]
    public async Task Gate_RejectsTamperedPortableFinalClosure()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);
        await File.AppendAllTextAsync(fixture.ClosurePath, "tampered");

        var result = await RunGateAsync(
            fixture,
            "offline",
            Path.Combine(_root, "tampered-closure.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("closure hash mismatch", result.Error);
    }

    [Fact]
    public async Task Gate_RequireReleaseEligibleReturnsTwoWithDiagnosticReport()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);
        var output = Path.Combine(_root, "required.json");

        var result = await RunGateAsync(
            fixture,
            "offline",
            output,
            requireReleaseEligible: true);

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(output));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        Assert.False(report.RootElement.GetProperty("release_eligible").GetBoolean());
    }

    [Fact]
    public async Task Gate_ProductionModeRecordsExternalConfigurationBlocker()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: false);
        var output = Path.Combine(_root, "production.json");

        var result = await RunGateAsync(fixture, "production", output);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        var root = report.RootElement;
        Assert.Equal("blocked_environment", root.GetProperty("policy_status").GetString());
        Assert.Equal("blocked_environment", root.GetProperty("service_status").GetString());
        Assert.Contains(
            root.GetProperty("environment_blockers").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "production-marketplace");
    }

    [Fact]
    public async Task Gate_PreflightValidatesWithoutWritingFiles()
    {
        var fixture = await WriteProductAcceptanceAsync(blocked: true);

        var result = await RunGateAsync(
            fixture,
            "offline",
            preflightOnly: true);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output);
        AssertSchemaValid(report);
        Assert.True(report.RootElement.GetProperty("preflight_only").GetBoolean());
        Assert.Empty(Directory.GetFiles(_root, "preflight.json"));
        Assert.False(Directory.Exists(Path.Combine(_root, "preflight.sources")));
    }

    private async Task<ProductFixture> WriteProductAcceptanceAsync(bool blocked)
    {
        Directory.CreateDirectory(_root);
        var commit = await GitAsync("rev-parse", "HEAD");
        var dirty = (await GitAsync("status", "--porcelain", "--untracked-files=no"))
            .Length > 0;
        var hostPath = Path.Combine(
            FindRoot(),
            "src",
            "LongBetterWindows.Host",
            "bin",
            "Release",
            "net8.0-windows",
            "LongBetterWindows.Host.exe");
        Assert.True(File.Exists(hostPath));
        var closureDirectory = Path.Combine(_root, "product.sources");
        Directory.CreateDirectory(closureDirectory);
        var closurePath = Path.Combine(closureDirectory, "final-closure.json");
        var closure = new
        {
            schema_version = 2,
            classification = "final_closure",
            source_commit = commit,
            source_dirty = dirty,
        };
        await File.WriteAllTextAsync(closurePath, JsonSerializer.Serialize(closure));
        var hostHash = Hash(await File.ReadAllBytesAsync(hostPath));
        var product = new
        {
            schema_version = 3,
            classification = "automated_final_product_acceptance",
            source_commit = commit,
            source_dirty = dirty,
            acceptance_status = blocked ? "blocked_environment" : "passed",
            plugin_count = 25,
            command_count = 42,
            automated_gate_count = 94,
            passed_gate_count = blocked ? 93 : 94,
            failed_gate_count = 0,
            environment_blocked_gate_count = blocked ? 1 : 0,
            not_run_gate_count = 0,
            not_applicable_gate_count = 0,
            contract_valid = true,
            release_eligible = !blocked && !dirty,
            release_host = new { path = hostPath, sha256 = hostHash },
            final_closure = new
            {
                file = "product.sources/final-closure.json",
                sha256 = Hash(await File.ReadAllBytesAsync(closurePath)),
            },
            environment_blockers = blocked
                ? new object[]
                {
                    new
                    {
                        gate_id = "native-performance-preflight",
                        reason = "administrator is unavailable",
                    },
                }
                : Array.Empty<object>(),
        };
        var productPath = Path.Combine(_root, "product.json");
        await File.WriteAllTextAsync(productPath, JsonSerializer.Serialize(product));
        return new ProductFixture(productPath, closurePath, hostPath);
    }

    private async Task<ProcessResult> RunGateAsync(
        ProductFixture fixture,
        string serviceMode,
        string? output = null,
        string? endpoint = null,
        string? certificateSha256 = null,
        string distributionChannel = "unsigned",
        bool requireReleaseEligible = false,
        bool preflightOnly = false)
    {
        var arguments = new List<string>
        {
            "-File",
            Path.Combine(FindRoot(), "verify-external-release-gate.ps1"),
            "-ProductAcceptanceGatePath",
            fixture.ProductPath,
            "-SubjectExecutable",
            fixture.HostPath,
            "-ExpectedSourceCommit",
            await GitAsync("rev-parse", "HEAD"),
            "-ExpectedDistributionChannel",
            distributionChannel,
            "-ServiceMode",
            serviceMode,
            "-AllowDirty",
        };
        if (output is not null)
        {
            arguments.Add("-OutputPath");
            arguments.Add(output);
        }
        if (endpoint is not null)
        {
            arguments.Add("-TestServiceEndpoint");
            arguments.Add(endpoint);
        }
        if (certificateSha256 is not null)
        {
            arguments.Add("-TestServiceCertificateSha256");
            arguments.Add(certificateSha256);
        }
        if (preflightOnly)
            arguments.Add("-PreflightOnly");
        if (requireReleaseEligible)
            arguments.Add("-RequireReleaseEligible");
        return await RunPowerShellAsync(arguments);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = FindRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<string> GitAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = FindRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        return output.Trim();
    }

    private static void AssertSchemaValid(JsonDocument report)
    {
        var evaluation = ReportSchema.Value.Evaluate(
            report.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, JsonSerializer.Serialize(evaluation.Details));
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(FindRoot(), name));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record ProductFixture(
        string ProductPath,
        string ClosurePath,
        string HostPath);

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class LoopbackMarketplaceServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _registry;
        private readonly byte[] _packageResponse;
        private readonly X509Certificate2 _certificate;
        private readonly List<string> _requestPaths = [];

        public LoopbackMarketplaceServer(
            string sourceCommit,
            byte[] package,
            bool tamperPackageResponse = false)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=127.0.0.1",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var ephemeralCertificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(1));
            const string password = "long-a007-loopback";
#pragma warning disable SYSLIB0057
            _certificate = new X509Certificate2(
                ephemeralCertificate.Export(
                    X509ContentType.Pfx,
                    password),
                password,
                X509KeyStorageFlags.UserKeySet |
                X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
            CertificateSha256 = Hash(_certificate.RawData);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Endpoint = $"https://127.0.0.1:{port}/";
            var registry = new
            {
                schema_version = 1,
                classification = "long_marketplace_test_service_registry",
                source_commit = sourceCommit,
                capabilities = new[]
                {
                    "registry_fetch",
                    "package_fetch",
                    "rollback",
                    "offline_fallback",
                },
                package = new
                {
                    path = "package.bin",
                    sha256 = Hash(package),
                    bytes = package.Length,
                },
            };
            _registry = JsonSerializer.SerializeToUtf8Bytes(registry);
            _packageResponse = tamperPackageResponse
                ? Encoding.UTF8.GetBytes("tampered package response")
                : package;
            Completion = ServeAsync();
        }

        public string Endpoint { get; }
        public string CertificateSha256 { get; }
        public Task Completion { get; }
        public IReadOnlyList<string> RequestPaths => _requestPaths;

        private async Task ServeAsync()
        {
            try
            {
                for (var request = 0; request < 2; request++)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    await using var network = client.GetStream();
                    await using var stream = new SslStream(network, leaveInnerStreamOpen: false);
                    await stream.AuthenticateAsServerAsync(
                        _certificate,
                        clientCertificateRequired: false,
                        SslProtocols.Tls12 | SslProtocols.Tls13,
                        checkCertificateRevocation: false);
                    var header = await ReadHeaderAsync(stream);
                    var firstLine = header.Split("\r\n", StringSplitOptions.None)[0];
                    var path = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
                    _requestPaths.Add(path);
                    var body = path == "/registry.json"
                        ? _registry
                        : _packageResponse;
                    var responseHeader = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n" +
                        "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeader);
                    await stream.WriteAsync(body);
                    await stream.FlushAsync();
                }
            }
            catch (SocketException) when (!_listener.Server.IsBound)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task<string> ReadHeaderAsync(Stream stream)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            while (bytes.Count < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;
                bytes.Add(buffer[0]);
                if (bytes.Count >= 4 &&
                    bytes[^4] == '\r' &&
                    bytes[^3] == '\n' &&
                    bytes[^2] == '\r' &&
                    bytes[^1] == '\n')
                    break;
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _certificate.Dispose();
            try
            {
                await Completion.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }
}
