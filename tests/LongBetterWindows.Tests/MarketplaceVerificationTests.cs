using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.MarketplacePublisher;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceVerificationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-market-verify-tests-{Guid.NewGuid():N}");

    public MarketplaceVerificationTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task VerifyAsync_PublicClientPathValidatesSignedPackageAndWritesSafeReport()
    {
        var fixture = await CreateFixtureAsync();
        var handler = new PublicMarketplaceHandler(fixture.Storage);
        using var client = new HttpClient(handler);
        var reportPath = Path.Combine(_tempDir, "reports", "verification.json");

        var report = await new MarketplaceVerificationPipeline().VerifyAsync(new MarketplaceVerificationOptions
        {
            RegistryUri = new Uri("https://market.example/registry.json"),
            TrustStorePath = fixture.TrustPath,
            AllowedPackageHosts = new[] { "market.example" },
            ReportPath = reportPath,
        }, client);

        Assert.Equal(1, report.EntryCount);
        Assert.Equal(1, report.PackageCount);
        Assert.True(report.TotalPackageBytes > 0);
        Assert.Equal(1, report.TrustedPublisherKeyCount);
        Assert.All(handler.Requests, request => Assert.Null(request.Authorization));
        var reportJson = await File.ReadAllTextAsync(reportPath);
        Assert.DoesNotContain("PublicKeyPem", reportJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signature", reportJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_TamperedRemotePackageIsRejected()
    {
        var fixture = await CreateFixtureAsync();
        var packagePath = fixture.Storage.Keys.Single(x => x.EndsWith(".lpak", StringComparison.OrdinalIgnoreCase));
        fixture.Storage[packagePath] = fixture.Storage[packagePath].Concat(Encoding.UTF8.GetBytes("tampered")).ToArray();
        using var client = new HttpClient(new PublicMarketplaceHandler(fixture.Storage));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplaceVerificationPipeline().VerifyAsync(CreateOptions(fixture.TrustPath), client));

        Assert.Contains("SHA-256", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_PublisherOutsideTrustStoreIsRejected()
    {
        var fixture = await CreateFixtureAsync();
        using var unrelated = RSA.Create(2048);
        var unrelatedTrust = Path.Combine(_tempDir, "unrelated-trust.json");
        await WriteTrustAsync(unrelatedTrust, "unrelated-key", unrelated);
        using var client = new HttpClient(new PublicMarketplaceHandler(fixture.Storage));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplaceVerificationPipeline().VerifyAsync(CreateOptions(unrelatedTrust), client));

        Assert.Contains("trust", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_RegistryRedirectIsRejected()
    {
        var fixture = await CreateFixtureAsync();
        var handler = new PublicMarketplaceHandler(fixture.Storage) { RedirectRegistry = true };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplaceVerificationPipeline().VerifyAsync(CreateOptions(fixture.TrustPath), client));

        Assert.Single(handler.Requests);
    }

    private MarketplaceVerificationOptions CreateOptions(string trustPath) => new()
    {
        RegistryUri = new Uri("https://market.example/registry.json"),
        TrustStorePath = trustPath,
        AllowedPackageHosts = new[] { "market.example" },
    };

    private async Task<VerificationFixture> CreateFixtureAsync()
    {
        var packages = Path.Combine(_tempDir, $"packages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packages);
        var packagePath = Path.Combine(packages, "verification.lpak");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new
            {
                id = "dev.long.verification",
                version = "1.0.0",
                name = "Verification",
                author = "Long",
                runtime = "webview",
                entry_point = "index.html",
                capabilities = Array.Empty<string>(),
                min_host_version = "0.5.0",
                min_api_version = "1.0.0",
                min_ui_kit_version = "1.0.0",
            }));
            WriteEntry(archive, "index.html", "<!doctype html><title>verification</title>");
        }

        using var rsa = RSA.Create(2048);
        var privateKey = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.private.pem");
        await File.WriteAllTextAsync(privateKey, rsa.ExportPkcs8PrivateKeyPem());
        var sourcePath = Path.Combine(_tempDir, $"source-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(sourcePath, JsonSerializer.Serialize(new MarketplacePublishSource
        {
            Entries = new[]
            {
                new MarketplacePublishEntry
                {
                    Id = "dev.long.verification",
                    Name = "Verification",
                    Summary = "Verification fixture",
                    Category = "Tests",
                    Versions = new[]
                    {
                        new MarketplacePublishVersion { Version = "1.0.0", PackageFile = "verification.lpak" },
                    },
                },
            },
        }));
        var bundle = Path.Combine(_tempDir, $"bundle-{Guid.NewGuid():N}");
        await new MarketplacePublishingPipeline().PublishAsync(new MarketplacePublishOptions
        {
            SourceCatalogPath = sourcePath,
            PackagesDirectory = packages,
            OutputDirectory = bundle,
            PrivateKeyPath = privateKey,
            PublisherKeyId = "verification-key",
            PublisherName = "Long Verification Tests",
            BasePackageUri = new Uri("https://market.example/packages/"),
        });

        var storage = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["/registry.json"] = await File.ReadAllBytesAsync(Path.Combine(bundle, "registry.json")),
        };
        foreach (var file in Directory.GetFiles(Path.Combine(bundle, "packages"), "*.lpak"))
            storage["/packages/" + Path.GetFileName(file)] = await File.ReadAllBytesAsync(file);
        return new VerificationFixture(
            storage, Path.Combine(bundle, "trusted-publisher.fragment.json"));
    }

    private static async Task WriteTrustAsync(string path, string keyId, RSA rsa)
    {
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new TrustedPublisherDocument
        {
            Publishers = new[]
            {
                new TrustedPublisherKey
                {
                    KeyId = keyId,
                    Publisher = "Unrelated",
                    PublicKeyPem = publicKey,
                    Sha256Fingerprint = fingerprint,
                },
            },
        }));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed record VerificationFixture(Dictionary<string, byte[]> Storage, string TrustPath);
    private sealed class PublicMarketplaceHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _storage;
        public List<(string Path, string? Authorization)> Requests { get; } = new();
        public bool RedirectRegistry { get; init; }
        public PublicMarketplaceHandler(Dictionary<string, byte[]> storage) => _storage = storage;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, request.Headers.Authorization?.ToString()));
            if (RedirectRegistry && path == "/registry.json")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request,
                    Headers = { Location = new Uri("https://other.example/registry.json") },
                });
            if (_storage.TryGetValue(path, out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(bytes),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
