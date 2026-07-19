using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceTransportTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-market-transport-{Guid.NewGuid():N}");

    public MarketplaceTransportTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void RemoteRepository_RejectsPlainHttpRegistry()
    {
        using var client = new HttpClient(new DelegateHandler(_ => Response("{}")));

        Assert.Throws<ArgumentException>(() => new RemoteMarketplaceRepository(
            client, new Uri("http://registry.example/registry.json"),
            Path.Combine(_tempDir, "cache.json")));
    }

    [Fact]
    public async Task RemoteRepository_UsesLastValidCacheWhenNetworkFails()
    {
        var catalogJson = JsonSerializer.Serialize(new MarketplaceCatalog
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            GeneratedAt = DateTimeOffset.UtcNow,
            Entries = new[] { Entry() },
        });
        var fail = false;
        using var client = new HttpClient(new DelegateHandler(_ =>
            fail ? throw new HttpRequestException("offline") : Response(catalogJson)));
        var repository = new RemoteMarketplaceRepository(
            client, new Uri("https://registry.example/registry.json"),
            Path.Combine(_tempDir, "registry-cache.json"));

        var online = await repository.LoadAsync();
        fail = true;
        var offline = await repository.LoadAsync();

        Assert.True(online.IsSuccess, online.Error);
        Assert.True(offline.IsSuccess, offline.Error);
        Assert.True(offline.IsFallback);
        Assert.Contains("上次可信目录", offline.Status);
        Assert.Equal(MarketplaceSourceKind.RemoteRegistry, offline.Catalog!.Entries[0].Source);
    }

    [Fact]
    public async Task Downloader_HashMismatchRejectsAndRemovesTemporaryFile()
    {
        using var client = new HttpClient(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")),
            }));
        var cache = Path.Combine(_tempDir, "packages");
        var downloader = new MarketplacePackageDownloader(
            client, cache, new[] { "packages.example" });
        var version = new MarketplacePackageVersion
        {
            Version = "1.0.0",
            PackageUri = new Uri("https://packages.example/test.lpak"),
            Sha256 = new string('0', 64),
        };

        var result = await downloader.DownloadAsync("dev.long.test", version);

        Assert.False(result.IsSuccess);
        Assert.Contains("SHA-256", result.Error);
        Assert.Empty(Directory.GetFiles(cache));
    }

    [Fact]
    public async Task Downloader_RetriesTransientFailureAndCleansStaleTemporaryFile()
    {
        var bytes = CreateWebPackage();
        var requests = 0;
        using var client = new HttpClient(new DelegateHandler(_ =>
        {
            if (Interlocked.Increment(ref requests) == 1)
                throw new HttpRequestException("temporary offline");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
        }));
        var cache = Path.Combine(_tempDir, "retry-cache");
        Directory.CreateDirectory(cache);
        var stale = Path.Combine(cache, ".download-stale.tmp");
        await File.WriteAllTextAsync(stale, "partial");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
        var downloader = new MarketplacePackageDownloader(
            client, cache, new[] { "packages.example" },
            maximumAttempts: 3, retryDelay: TimeSpan.Zero);
        var version = DownloadVersion(bytes);

        var result = await downloader.DownloadAsync("dev.long.retry", version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, requests);
        Assert.False(File.Exists(stale));
        Assert.Empty(Directory.GetFiles(cache, ".download-*.tmp"));
    }

    [Fact]
    public async Task Downloader_CoalescesConcurrentRequestsForTheSamePackage()
    {
        var bytes = CreateWebPackage();
        var requests = 0;
        using var client = new HttpClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            await Task.Delay(80, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
        }));
        var downloader = new MarketplacePackageDownloader(
            client, Path.Combine(_tempDir, "coalesced-cache"),
            new[] { "packages.example" }, maximumAttempts: 1);
        var version = DownloadVersion(bytes);

        var results = await Task.WhenAll(
            downloader.DownloadAsync("dev.long.concurrent", version),
            downloader.DownloadAsync("dev.long.concurrent", version));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Equal(1, requests);
        Assert.Single(results, result => result.FromCache);
        Assert.Single(results, result => !result.FromCache);
    }

    [Fact]
    public async Task Downloader_ReportsTimeoutAfterBoundedAttemptsAndRemovesPartials()
    {
        var bytes = CreateWebPackage();
        var requests = 0;
        using var client = new HttpClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requests);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }));
        var cache = Path.Combine(_tempDir, "timeout-cache");
        var downloader = new MarketplacePackageDownloader(
            client, cache, new[] { "packages.example" },
            TimeSpan.FromMilliseconds(20), maximumAttempts: 2,
            retryDelay: TimeSpan.Zero);

        var result = await downloader.DownloadAsync(
            "dev.long.timeout", DownloadVersion(bytes));

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, requests);
        Assert.Contains("超时", result.Error);
        Assert.Empty(Directory.GetFiles(cache));
    }

    [Fact]
    public async Task SignedRemotePackage_DownloadTrustAndValidationChainPasses()
    {
        var packageBytes = CreateWebPackage();
        var hash = SHA256.HashData(packageBytes);
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        var trustPath = Path.Combine(_tempDir, "trusted-publishers.json");
        var trustDocument = new TrustedPublisherDocument
        {
            Publishers = new[]
            {
                new TrustedPublisherKey
                {
                    KeyId = "long-test-root-1",
                    Publisher = "Long Test Publisher",
                    PublicKeyPem = publicKey,
                    Sha256Fingerprint = fingerprint,
                },
            },
        };
        await File.WriteAllTextAsync(trustPath, JsonSerializer.Serialize(trustDocument));
        var trust = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(trustPath);
        Assert.True(trust.IsSuccess, trust.Error);

        using var client = new HttpClient(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes),
            }));
        var version = new MarketplacePackageVersion
        {
            Version = "1.0.0",
            PackageUri = new Uri("https://packages.example/dev.long.remote.lpak"),
            Sha256 = Convert.ToHexString(hash),
            Signature = Convert.ToBase64String(rsa.SignHash(
                hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            PublisherPublicKeyPem = publicKey,
            PublisherKeyId = "long-test-root-1",
        };
        var downloader = new MarketplacePackageDownloader(
            client, Path.Combine(_tempDir, "signed-cache"),
            new[] { "packages.example" });

        var download = await downloader.DownloadAsync("dev.long.remote", version);
        Assert.True(download.IsSuccess, download.Error);
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            ExpectedPluginId = "dev.long.remote",
            ExpectedVersion = version.Version,
            ExpectedSha256 = version.Sha256,
            Signature = version.Signature,
            PublisherPublicKeyPem = version.PublisherPublicKeyPem,
            PublisherKeyId = version.PublisherKeyId,
        };
        var validation = await new PluginPackageValidator(
            new Version(1, 0, 0), trustStore: trust.Store)
            .ValidateAsync(download.PackagePath!, metadata);

        Assert.True(validation.IsSuccess, validation.Error);
        Assert.Equal(PackageTrustLevel.PublisherSigned, validation.TrustLevel);

        var cached = await downloader.DownloadAsync("dev.long.remote", version);
        Assert.True(cached.IsSuccess);
        Assert.True(cached.FromCache);
    }

    [Fact]
    public async Task TrustStore_FingerprintMismatch_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(_tempDir, "bad-trust.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new TrustedPublisherDocument
        {
            Publishers = new[]
            {
                new TrustedPublisherKey
                {
                    KeyId = "bad",
                    Publisher = "Bad",
                    PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
                    Sha256Fingerprint = new string('0', 64),
                },
            },
        }));

        var result = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Contains("指纹不匹配", result.Error);
    }

    [Fact]
    public async Task SignedPackage_InstallUpgradeDowngradeAndUninstallTransactionPasses()
    {
        var pluginId = $"dev.long.transaction.{Guid.NewGuid():N}";
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var trustStore = new PublisherTrustStore(new Dictionary<string, string>
        {
            ["transaction-root"] = publicKey,
        });
        var pluginsDir = Path.Combine(_tempDir, "transaction-plugins");
        using var scanner = new PluginScanner(pluginsDir);
        var installer = new LpakInstaller(scanner, pluginsDir);
        installer.ConfigureTrustStore(trustStore);

        async Task<(string Path, MarketplacePackageMetadata Metadata)> SignedPackage(string version)
        {
            var bytes = CreateWebPackage(pluginId, version);
            var path = Path.Combine(_tempDir, $"{version}.lpak");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = SHA256.HashData(bytes);
            return (path, new MarketplacePackageMetadata
            {
                Source = MarketplaceSourceKind.RemoteRegistry,
                ExpectedPluginId = pluginId,
                ExpectedVersion = version,
                ExpectedSha256 = Convert.ToHexString(hash),
                Signature = Convert.ToBase64String(rsa.SignHash(
                    hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
                PublisherPublicKeyPem = publicKey,
                PublisherKeyId = "transaction-root",
            });
        }

        var v1 = await SignedPackage("1.0.0");
        var install = await installer.InstallAsync(v1.Path, v1.Metadata);
        Assert.True(install.IsSuccess, install.Error);
        Assert.Equal("1.0.0", HostProvider.Instance.PluginStore.Get(pluginId)?.Manifest.Version);

        var v2 = await SignedPackage("2.0.0");
        var upgrade = await installer.InstallAsync(v2.Path, v2.Metadata);
        Assert.True(upgrade.IsSuccess, upgrade.Error);
        Assert.Equal(InstallAction.Replace, upgrade.Action);
        Assert.Equal("2.0.0", HostProvider.Instance.PluginStore.Get(pluginId)?.Manifest.Version);

        var downgrade = await installer.InstallAsync(v1.Path, v1.Metadata);
        Assert.True(downgrade.IsSuccess, downgrade.Error);
        Assert.Equal("1.0.0", HostProvider.Instance.PluginStore.Get(pluginId)?.Manifest.Version);

        var uninstall = await installer.UninstallAsync(pluginId);
        Assert.True(uninstall.IsSuccess, uninstall.Error);
        Assert.Null(HostProvider.Instance.PluginStore.Get(pluginId));
        Assert.False(Directory.Exists(Path.Combine(pluginsDir, pluginId.Replace('.', '-'))));
    }

    [Fact]
    public async Task InterruptedTransactions_RestoreOnlyUncommittedStateAndRemoveJournals()
    {
        var pluginsDir = Path.Combine(_tempDir, "recovery-plugins");
        Directory.CreateDirectory(pluginsDir);
        using var scanner = new PluginScanner(pluginsDir);
        var installer = new LpakInstaller(scanner, pluginsDir);
        var parent = Directory.GetParent(pluginsDir)!.FullName;

        async Task WritePlugin(string directory, string id, string version)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    id,
                    version,
                    name = "Recovery Test",
                    author = "Long",
                    runtime = "webview",
                    entry_point = "index.html",
                    capabilities = Array.Empty<string>(),
                }));
            await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), version);
        }

        async Task<string> CreateTransaction(string id, int phase, string? backupVersion)
        {
            var transaction = Path.Combine(parent, $".long-transaction-{Guid.NewGuid():N}");
            Directory.CreateDirectory(transaction);
            await File.WriteAllTextAsync(Path.Combine(transaction, "journal.json"),
                JsonSerializer.Serialize(new { PluginId = id, Phase = phase }));
            if (backupVersion != null)
                await WritePlugin(Path.Combine(transaction, "backup"), id, backupVersion);
            return transaction;
        }

        const string replacedId = "dev.long.recovery.replace";
        var replacedTarget = Path.Combine(pluginsDir, "dev-long-recovery-replace");
        await WritePlugin(replacedTarget, replacedId, "2.0.0");
        var replaceTransaction = await CreateTransaction(replacedId, 1, "1.0.0");

        const string firstInstallId = "dev.long.recovery.first";
        var firstInstallTarget = Path.Combine(pluginsDir, "dev-long-recovery-first");
        await WritePlugin(firstInstallTarget, firstInstallId, "1.0.0");
        var firstTransaction = await CreateTransaction(firstInstallId, 0, null);

        const string committedId = "dev.long.recovery.committed";
        var committedTarget = Path.Combine(pluginsDir, "dev-long-recovery-committed");
        await WritePlugin(committedTarget, committedId, "2.0.0");
        var committedTransaction = await CreateTransaction(committedId, 2, "1.0.0");

        var recovered = await installer.RecoverInterruptedTransactionsAsync();

        Assert.Equal(3, recovered);
        var restored = await ManifestReader.ReadAsync(replacedTarget);
        Assert.True(restored.IsSuccess, restored.Error);
        Assert.Equal("1.0.0", restored.Manifest!.Version);
        Assert.False(Directory.Exists(firstInstallTarget));
        var committed = await ManifestReader.ReadAsync(committedTarget);
        Assert.True(committed.IsSuccess, committed.Error);
        Assert.Equal("2.0.0", committed.Manifest!.Version);
        Assert.False(Directory.Exists(replaceTransaction));
        Assert.False(Directory.Exists(firstTransaction));
        Assert.False(Directory.Exists(committedTransaction));
    }

    [Fact]
    public async Task ConcurrentInstallRequests_AreSerializedWithoutTransactionResidue()
    {
        var pluginId = $"dev.long.concurrent.install.{Guid.NewGuid():N}";
        var pluginsDir = Path.Combine(_tempDir, "concurrent-install-plugins");
        var v1 = Path.Combine(_tempDir, "concurrent-v1.lpak");
        var v2 = Path.Combine(_tempDir, "concurrent-v2.lpak");
        await File.WriteAllBytesAsync(v1, CreateWebPackage(pluginId, "1.0.0"));
        await File.WriteAllBytesAsync(v2, CreateWebPackage(pluginId, "2.0.0"));
        using var scanner = new PluginScanner(pluginsDir);
        var installer = new LpakInstaller(scanner, pluginsDir);

        var results = await Task.WhenAll(
            installer.InstallAsync(v1),
            installer.InstallAsync(v2));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        var target = Path.Combine(pluginsDir, pluginId.Replace('.', '-'));
        var installed = await ManifestReader.ReadAsync(target);
        Assert.True(installed.IsSuccess, installed.Error);
        Assert.Contains(installed.Manifest!.Version, new[] { "1.0.0", "2.0.0" });
        var parent = Directory.GetParent(pluginsDir)!.FullName;
        Assert.Empty(Directory.GetDirectories(parent, ".long-transaction-*"));

        var uninstall = await installer.UninstallAsync(pluginId);
        Assert.True(uninstall.IsSuccess, uninstall.Error);
    }

    private static MarketplaceEntry Entry() => new()
    {
        Id = "dev.long.remote",
        Name = "Remote Test",
        Summary = "Remote catalog entry",
        Publisher = "Long",
        Category = "Tests",
        Versions = new[] { new MarketplacePackageVersion { Version = "1.0.0" } },
    };

    private static MarketplacePackageVersion DownloadVersion(byte[] bytes) => new()
    {
        Version = "1.0.0",
        PackageUri = new Uri("https://packages.example/test.lpak"),
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
    };

    private static byte[] CreateWebPackage(
        string pluginId = "dev.long.remote", string version = "1.0.0")
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new
            {
                id = pluginId,
                version,
                name = "Signed Remote Test",
                author = "Long",
                runtime = "webview",
                entry_point = "index.html",
                capabilities = Array.Empty<string>(),
                min_host_version = "0.5.0",
                min_api_version = "1.0.0",
                min_ui_kit_version = "1.0.0",
            }));
            WriteEntry(archive, "index.html", "<!doctype html><title>signed</title>");
        }
        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static HttpResponseMessage Response(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = (request, _) => Task.FromResult(handler(request));
        public DelegateHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
