using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.MarketplacePublisher;

namespace LongBetterWindows.Tests;

public sealed class MarketplacePublisherTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-market-publisher-{Guid.NewGuid():N}");

    public MarketplacePublisherTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task PublishAsync_GeneratesVerifiableBundleWithoutPrivateKeyLeak()
    {
        var fixture = await CreateFixtureAsync();

        var result = await new MarketplacePublishingPipeline().PublishAsync(fixture.Options);

        Assert.Equal(1, result.PackageCount);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "registry.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "publish-report.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "trusted-publisher.fragment.json")));
        var packages = Directory.GetFiles(Path.Combine(result.OutputDirectory, "packages"), "*.lpak");
        var package = Assert.Single(packages);

        var privatePem = await File.ReadAllTextAsync(fixture.PrivateKeyPath);
        foreach (var textFile in Directory.GetFiles(result.OutputDirectory, "*.json"))
            Assert.DoesNotContain("PRIVATE KEY", await File.ReadAllTextAsync(textFile));
        Assert.DoesNotContain(privatePem, await File.ReadAllTextAsync(
            Path.Combine(result.OutputDirectory, "registry.json")));

        var catalogResult = await new LocalMarketplaceRepository(
            Path.Combine(result.OutputDirectory, "registry.json")).LoadAsync();
        Assert.True(catalogResult.IsSuccess, catalogResult.Error);
        var entry = Assert.Single(catalogResult.Catalog!.Entries);
        var version = Assert.Single(entry.Versions);
        Assert.Equal("system.clipboard", Assert.Single(version.Capabilities));
        Assert.Equal("0.5.0", version.MinHostVersion);
        Assert.Equal(new Uri("https://packages.example/"), new Uri(version.PackageUri!, "."));

        var trust = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(
            Path.Combine(result.OutputDirectory, "trusted-publisher.fragment.json"));
        Assert.True(trust.IsSuccess, trust.Error);
        var validation = await new PluginPackageValidator(trustStore: trust.Store).ValidateAsync(
            package,
            new MarketplacePackageMetadata
            {
                Source = MarketplaceSourceKind.RemoteRegistry,
                ExpectedPluginId = entry.Id,
                ExpectedVersion = version.Version,
                ExpectedSha256 = version.Sha256,
                Signature = version.Signature,
                PublisherPublicKeyPem = version.PublisherPublicKeyPem,
                PublisherKeyId = version.PublisherKeyId,
            });
        Assert.True(validation.IsSuccess, validation.Error);
        Assert.Equal(PackageTrustLevel.PublisherSigned, validation.TrustLevel);
    }

    [Fact]
    public async Task VerifyBundleAsync_Uses_exported_trust_root_and_writes_sanitized_report()
    {
        var fixture = await CreateFixtureAsync();
        var published = await new MarketplacePublishingPipeline().PublishAsync(fixture.Options);
        var reportPath = Path.Combine(_tempDir, "bundle-verification.json");

        var report = await new MarketplaceBundleVerificationPipeline().VerifyAsync(
            new MarketplaceBundleVerificationOptions
            {
                BundleDirectory = published.OutputDirectory,
                ExpectedPublisherKeyId = published.PublisherKeyId,
                ExpectedPublicKeyFingerprint = published.PublicKeyFingerprint,
                ReportPath = reportPath,
            });

        Assert.Equal(1, report.PackageCount);
        Assert.Equal("publisher-test-root", report.PublisherKeyId);
        Assert.Equal(64, report.PublicKeyFingerprint.Length);
        var json = await File.ReadAllTextAsync(reportPath);
        Assert.DoesNotContain("Signature", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PublicKeyPem", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE KEY", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyBundleAsync_Rejects_registry_publisher_outside_exported_trust_root()
    {
        var fixture = await CreateFixtureAsync();
        var published = await new MarketplacePublishingPipeline().PublishAsync(fixture.Options);
        var trustPath = Path.Combine(published.OutputDirectory, "trusted-publisher.fragment.json");
        var trust = await File.ReadAllTextAsync(trustPath);
        await File.WriteAllTextAsync(trustPath, trust.Replace(
            "publisher-test-root", "unrelated-root", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplaceBundleVerificationPipeline().VerifyAsync(
                new MarketplaceBundleVerificationOptions
                {
                    BundleDirectory = published.OutputDirectory,
                    ExpectedPublisherKeyId = published.PublisherKeyId,
                    ExpectedPublicKeyFingerprint = published.PublicKeyFingerprint,
                }));
    }

    [Fact]
    public async Task PublishAsync_ExistingOutputRequiresForceAndPreservesOldOutputOnRejection()
    {
        var fixture = await CreateFixtureAsync();
        Directory.CreateDirectory(fixture.Options.OutputDirectory);
        var sentinel = Path.Combine(fixture.Options.OutputDirectory, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "keep");

        await Assert.ThrowsAsync<IOException>(() =>
            new MarketplacePublishingPipeline().PublishAsync(fixture.Options));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));

        var forced = CloneOptions(fixture.Options, force: true);
        var result = await new MarketplacePublishingPipeline().PublishAsync(forced);
        Assert.False(File.Exists(sentinel));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "registry.json")));
        Assert.Empty(Directory.GetDirectories(_tempDir, ".market-backup-*"));
        Assert.Empty(Directory.GetDirectories(_tempDir, ".market-publish-*"));
    }

    [Fact]
    public async Task PublishAsync_PackagePathTraversalIsRejectedWithoutOutput()
    {
        var fixture = await CreateFixtureAsync(packageFile: "../outside.lpak");
        File.Copy(fixture.PackagePath, Path.Combine(_tempDir, "outside.lpak"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplacePublishingPipeline().PublishAsync(fixture.Options));

        Assert.Contains("escapes", error.Message);
        Assert.False(Directory.Exists(fixture.Options.OutputDirectory));
        Assert.Empty(Directory.GetDirectories(_tempDir, ".market-publish-*"));
    }

    [Fact]
    public async Task PublishAsync_OutputContainingPrivateKeyIsRejectedBeforeMutation()
    {
        var fixture = await CreateFixtureAsync();
        var unsafeOutput = Path.GetDirectoryName(fixture.PrivateKeyPath)!;
        var options = new MarketplacePublishOptions
        {
            SourceCatalogPath = fixture.Options.SourceCatalogPath,
            PackagesDirectory = fixture.Options.PackagesDirectory,
            OutputDirectory = unsafeOutput,
            PrivateKeyPath = fixture.PrivateKeyPath,
            PublisherKeyId = fixture.Options.PublisherKeyId,
            PublisherName = fixture.Options.PublisherName,
            BasePackageUri = fixture.Options.BasePackageUri,
            Force = true,
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplacePublishingPipeline().PublishAsync(options));

        Assert.Contains("must not contain", error.Message);
        Assert.True(File.Exists(fixture.PrivateKeyPath));
        Assert.True(File.Exists(fixture.PackagePath));
    }

    private async Task<PublisherFixture> CreateFixtureAsync(string packageFile = "plugin-v1.0.0.lpak")
    {
        var packagesDir = Path.Combine(_tempDir, $"packages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packagesDir);
        var packagePath = Path.Combine(packagesDir, "plugin-v1.0.0.lpak");
        CreatePackage(packagePath);
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.private.pem");
        await File.WriteAllTextAsync(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        var sourcePath = Path.Combine(_tempDir, $"source-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(sourcePath, JsonSerializer.Serialize(new MarketplacePublishSource
        {
            Entries = new[]
            {
                new MarketplacePublishEntry
                {
                    Id = "dev.long.publisher-test",
                    Name = "Publisher Test",
                    Summary = "Pipeline fixture",
                    Description = "Signed by the publishing pipeline.",
                    Category = "Tests",
                    Tags = new[] { "signed" },
                    Versions = new[]
                    {
                        new MarketplacePublishVersion
                        {
                            Version = "1.0.0",
                            PackageFile = packageFile,
                            ReleaseNotes = "First signed build.",
                        },
                    },
                },
            },
        }));
        return new PublisherFixture
        {
            PackagePath = packagePath,
            PrivateKeyPath = privateKeyPath,
            Options = new MarketplacePublishOptions
            {
                SourceCatalogPath = sourcePath,
                PackagesDirectory = packagesDir,
                OutputDirectory = Path.Combine(_tempDir, $"output-{Guid.NewGuid():N}"),
                PrivateKeyPath = privateKeyPath,
                PublisherKeyId = "publisher-test-root",
                PublisherName = "Long Test Publisher",
                BasePackageUri = new Uri("https://packages.example/"),
            },
        };
    }

    private static MarketplacePublishOptions CloneOptions(
        MarketplacePublishOptions options, bool force) => new()
    {
        SourceCatalogPath = options.SourceCatalogPath,
        PackagesDirectory = options.PackagesDirectory,
        OutputDirectory = options.OutputDirectory,
        PrivateKeyPath = options.PrivateKeyPath,
        PublisherKeyId = options.PublisherKeyId,
        PublisherName = options.PublisherName,
        BasePackageUri = options.BasePackageUri,
        Force = force,
    };

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new
        {
            id = "dev.long.publisher-test",
            version = "1.0.0",
            name = "Publisher Test",
            author = "Long",
            runtime = "webview",
            entry_point = "index.html",
            capabilities = new[] { "system.clipboard" },
            min_host_version = "0.5.0",
            min_api_version = "1.0.0",
            min_ui_kit_version = "1.0.0",
        }));
        WriteEntry(archive, "index.html", "<!doctype html><title>publisher test</title>");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class PublisherFixture
    {
        public required string PackagePath { get; init; }
        public required string PrivateKeyPath { get; init; }
        public required MarketplacePublishOptions Options { get; init; }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
