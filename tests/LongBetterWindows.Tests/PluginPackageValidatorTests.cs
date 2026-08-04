using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginPackageValidatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-package-tests-{Guid.NewGuid():N}");

    public PluginPackageValidatorTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task ValidateAsync_ValidLocalPackage_ReturnsManifestAndLocalTrust()
    {
        var package = CreatePackage();

        var result = await new PluginPackageValidator(new Version(1, 0, 0))
            .ValidateAsync(package);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("dev.long.test", result.Manifest!.Id);
        Assert.Equal(PackageTrustLevel.LocalUnsigned, result.TrustLevel);
        Assert.True(result.RequiresHighTrustWarning);
        Assert.Equal(64, result.Sha256!.Length);
    }

    [Fact]
    public async Task ValidateAsync_InvalidManifest_PropagatesStableFailureContract()
    {
        var package = CreatePackage(manifestJson: "{");

        var result = await new PluginPackageValidator().ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Equal(ManifestErrorCode.InvalidJson, result.ManifestFailureCode);
        Assert.Empty(result.ManifestIssues);
    }

    [Fact]
    public async Task ValidateAsync_PathTraversalEntry_IsRejected()
    {
        var package = CreatePackage(extraEntryName: "../outside.txt");

        var result = await new PluginPackageValidator().ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("非法路径", result.Error);
        Assert.False(File.Exists(Path.Combine(_tempDir, "outside.txt")));
    }

    [Fact]
    public async Task ValidateAsync_CaseInsensitiveDuplicatePath_IsRejected()
    {
        var package = CreatePackage(extraEntryName: "PLUGIN.DLL");

        var result = await new PluginPackageValidator().ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("重复路径", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_RegistryHashMismatch_IsRejected()
    {
        var package = CreatePackage();
        var metadata = new MarketplacePackageMetadata
        {
            ExpectedSha256 = new string('0', 64),
        };

        var result = await new PluginPackageValidator().ValidateAsync(package, metadata);

        Assert.False(result.IsSuccess);
        Assert.Contains("SHA-256", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_RemoteUnsignedPackage_IsRejected()
    {
        var package = CreatePackage();
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
        };

        var result = await new PluginPackageValidator().ValidateAsync(package, metadata);

        Assert.False(result.IsSuccess);
        Assert.Contains("发布者签名", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_RemoteSignedPackage_IsTrusted()
    {
        var package = CreatePackage(runtime: "webview", entryPoint: "index.html");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(package));
        using var rsa = RSA.Create(2048);
        var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            ExpectedPluginId = "dev.long.test",
            ExpectedVersion = "1.0.0",
            ExpectedSha256 = Convert.ToHexString(hash),
            Signature = Convert.ToBase64String(signature),
            PublisherPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            PublisherKeyId = "test-key",
        };

        var trustStore = new PublisherTrustStore(new Dictionary<string, string>
        {
            ["test-key"] = rsa.ExportSubjectPublicKeyInfoPem(),
        });
        var result = await new PluginPackageValidator(
                new Version(1, 0, 0),
                trustStore: trustStore)
            .ValidateAsync(package, metadata);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(PackageTrustLevel.PublisherSigned, result.TrustLevel);
    }

    [Fact]
    public async Task ValidateAsync_RemoteSignedNativePackage_IsRejected()
    {
        var package = CreatePackage();
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(package));
        using var rsa = RSA.Create(2048);
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            ExpectedSha256 = Convert.ToHexString(hash),
            Signature = Convert.ToBase64String(rsa.SignHash(
                hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            PublisherPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            PublisherKeyId = "test-key",
        };
        var trustStore = new PublisherTrustStore(new Dictionary<string, string>
        {
            ["test-key"] = rsa.ExportSubjectPublicKeyInfoPem(),
        });

        var result = await new PluginPackageValidator(trustStore: trustStore)
            .ValidateAsync(package, metadata);

        Assert.False(result.IsSuccess);
        Assert.Contains("只允许纯 Web 插件", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_RemoteSignedHybridPackage_IsRejected()
    {
        var manifest = JsonSerializer.Serialize(new
        {
            id = "dev.long.test",
            version = "1.0.0",
            name = "Hybrid Plugin",
            runtime = "webview",
            entry_point = "index.html",
            background = new { entry_point = "Hybrid.Background.dll" },
            capabilities = Array.Empty<string>(),
        });
        var package = CreatePackage(
            extraEntryName: "Hybrid.Background.dll",
            runtime: "webview",
            entryPoint: "index.html",
            manifestJson: manifest);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(package));
        using var rsa = RSA.Create(2048);
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            ExpectedSha256 = Convert.ToHexString(hash),
            Signature = Convert.ToBase64String(rsa.SignHash(
                hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            PublisherPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            PublisherKeyId = "test-key",
        };
        var trustStore = new PublisherTrustStore(new Dictionary<string, string>
        {
            ["test-key"] = rsa.ExportSubjectPublicKeyInfoPem(),
        });

        var result = await new PluginPackageValidator(trustStore: trustStore)
            .ValidateAsync(package, metadata);

        Assert.False(result.IsSuccess);
        Assert.Contains("Hybrid", result.Error);
    }

    [Fact]
    public async Task DistributionPolicy_UnsignedWebPackageIsEligibleAfterSignature()
    {
        var package = CreatePackage(runtime: "webview", entryPoint: "index.html");
        var result = await new PluginPackageValidator().ValidateAsync(package);

        var distribution = PluginDistributionPolicy.Assess(result);

        Assert.True(distribution.LocalImportEligible);
        Assert.True(distribution.RemoteMarketplacePackageEligible);
        Assert.False(distribution.RemoteMarketplaceCurrentlyTrusted);
        Assert.True(distribution.RemoteMarketplaceRequiresPublisherSignature);
        Assert.Null(distribution.RemoteMarketplaceBlockReason);
    }

    [Fact]
    public async Task ValidateAsync_MinimumHostVersionTooNew_IsRejected()
    {
        var package = CreatePackage(minHostVersion: "2.0.0");

        var result = await new PluginPackageValidator(new Version(1, 5, 0))
            .ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("宿主版本不兼容", result.Error);
    }

    [Theory]
    [InlineData("1.1.0", true)]
    [InlineData("1.2.0", true)]
    [InlineData("1.3.0", true)]
    [InlineData("1.4.0", false)]
    public async Task ValidateAsync_UiKitMinorVersion_PreservesBackwardCompatibility(
        string minimumUiKitVersion,
        bool expectedSuccess)
    {
        var manifest = JsonSerializer.Serialize(new
        {
            id = "dev.long.ui-version",
            version = "1.0.0",
            name = "UI Version Plugin",
            runtime = "webview",
            entry_point = "index.html",
            min_ui_kit_version = minimumUiKitVersion,
            capabilities = Array.Empty<string>(),
        });
        var package = CreatePackage(
            runtime: "webview",
            entryPoint: "index.html",
            manifestJson: manifest);

        var result = await new PluginPackageValidator().ValidateAsync(package);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess) Assert.Contains("UI Kit", result.Error);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_UsesManifestEntryAndLocalizationRules()
    {
        var directory = CreatePluginDirectory();

        var result = await new PluginPackageValidator(new Version(1, 0, 0))
            .ValidateDirectoryAsync(directory);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("dev.long.directory", result.Manifest!.Id);
        Assert.Equal(PackageTrustLevel.LocalUnsigned, result.TrustLevel);
        Assert.False(result.RequiresHighTrustWarning);
        Assert.Null(result.Sha256);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_ScriptRequiresHighTrust()
    {
        var directory = Path.Combine(_tempDir, $"script-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                id = "dev.long.script",
                version = "1.0.0",
                name = "Script Plugin",
                runtime = "csharp-script",
                entry_point = "plugin.csx",
                capabilities = Array.Empty<string>(),
            }));
        await File.WriteAllTextAsync(
            Path.Combine(directory, "plugin.csx"),
            "Start = async () => await Task.CompletedTask;");

        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(directory);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.RequiresHighTrustWarning);
        var distribution = PluginDistributionPolicy.Assess(result);
        Assert.True(distribution.LocalImportEligible);
        Assert.False(distribution.RemoteMarketplacePackageEligible);
        Assert.Equal(
            PluginDistributionPolicy.HighTrustRuntimeNotSupported,
            distribution.RemoteMarketplaceBlockReason);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_MissingEntryIsRejectedBeforePackaging()
    {
        var directory = CreatePluginDirectory();
        File.Delete(Path.Combine(directory, "index.html"));

        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(directory);

        Assert.False(result.IsSuccess);
        Assert.Contains("入口文件不存在", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_MissingBackgroundBinaryIsRejected()
    {
        var manifest = JsonSerializer.Serialize(new
        {
            id = "dev.long.hybrid",
            version = "1.0.0",
            name = "Hybrid Plugin",
            runtime = "webview",
            entry_point = "index.html",
            background = new { entry_point = "Hybrid.Background.dll" },
            capabilities = Array.Empty<string>(),
        });
        var package = CreatePackage(
            runtime: "webview",
            entryPoint: "index.html",
            manifestJson: manifest);

        var result = await new PluginPackageValidator().ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("原生后台入口不存在", result.Error);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_WidgetResourcesMustExistWithinPluginRoot()
    {
        var directory = CreateWidgetPluginDirectory(includeWidgetEntry: false);

        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(directory);

        Assert.False(result.IsSuccess);
        Assert.Contains("Widget 入口不存在", result.Error);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_WidgetResourcesAreAccepted()
    {
        var directory = CreateWidgetPluginDirectory(includeWidgetEntry: true);

        var result = await new PluginPackageValidator()
            .ValidateDirectoryAsync(directory);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("dev.long.widget", result.Manifest!.Id);
        Assert.Single(result.Manifest.Widgets);
    }

    [Fact]
    public async Task ValidateAsync_ValidFileManifest_IsAccepted()
    {
        var package = CreatePackageWithFileManifest();

        var result = await new PluginPackageValidator(new Version(1, 0, 0))
            .ValidateAsync(package);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("dev.long.test", result.Manifest!.Id);
    }

    [Fact]
    public async Task ValidateAsync_TamperedFileManifestHash_IsRejected()
    {
        var package = CreatePackageWithFileManifest(tamperEntryHash: true);

        var result = await new PluginPackageValidator(new Version(1, 0, 0))
            .ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("SHA-256", result.Error);
    }

    [Fact]
    public async Task ValidateAsync_FileManifestIdentityMismatch_IsRejected()
    {
        var package = CreatePackageWithFileManifest(
            ledgerPluginId: "dev.long.someone-else");

        var result = await new PluginPackageValidator(new Version(1, 0, 0))
            .ValidateAsync(package);

        Assert.False(result.IsSuccess);
        Assert.Contains("manifest.json", result.Error);
    }

    [Fact]
    public void CreatePermissionDiff_SeparatesAddedRemovedAndUnchanged()
    {
        var diff = PluginPackageValidator.CreatePermissionDiff(
            new[] { "storage.local", "system.clipboard" },
            new[] { "storage.local", "network.http" });

        Assert.Equal(new[] { "network.http" }, diff.Added);
        Assert.Equal(new[] { "system.clipboard" }, diff.Removed);
        Assert.Equal(new[] { "storage.local" }, diff.Unchanged);
        Assert.True(diff.HasElevatedChanges);
    }

    [Fact]
    public async Task ValidateAsync_RemotePackageWithUnknownPublisher_IsRejected()
    {
        var package = CreatePackage();
        using var rsa = RSA.Create(2048);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(package));
        var metadata = new MarketplacePackageMetadata
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            Signature = Convert.ToBase64String(rsa.SignHash(
                hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            PublisherPublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            PublisherKeyId = "unknown-key",
        };

        var result = await new PluginPackageValidator().ValidateAsync(package, metadata);

        Assert.False(result.IsSuccess);
        Assert.Contains("信任库", result.Error);
    }

    private string CreatePackage(
        string? extraEntryName = null,
        string? minHostVersion = null,
        string runtime = "native",
        string entryPoint = "plugin.dll",
        string? manifestJson = null)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.lpak");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var manifest = new
        {
            id = "dev.long.test",
            version = "1.0.0",
            name = "Test Plugin",
            author = "Long",
            runtime,
            entry_point = entryPoint,
            capabilities = new[] { "storage.local" },
            min_host_version = minHostVersion,
        };
        WriteEntry(
            archive,
            "manifest.json",
            manifestJson ?? JsonSerializer.Serialize(manifest));
        WriteEntry(archive, entryPoint, "test-entry");
        if (extraEntryName != null) WriteEntry(archive, extraEntryName, "unsafe");
        return path;
    }

    private string CreatePluginDirectory()
    {
        var directory = Path.Combine(_tempDir, $"directory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "i18n"));
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                id = "dev.long.directory",
                version = "1.0.0",
                name = "Directory Plugin",
                description = "Directory validation fixture",
                runtime = "webview",
                entry_point = "index.html",
                capabilities = new[] { "storage.local" },
                localization = new
                {
                    default_language = "zh-CN",
                    resources = new Dictionary<string, string>
                    {
                        ["zh-CN"] = "i18n/zh-CN.json",
                    },
                },
            }));
        File.WriteAllText(Path.Combine(directory, "index.html"), "<html></html>");
        File.WriteAllText(
            Path.Combine(directory, "i18n", "zh-CN.json"),
            """{"title":"测试"}""");
        return directory;
    }

    private string CreateWidgetPluginDirectory(bool includeWidgetEntry)
    {
        var directory = Path.Combine(_tempDir, $"widget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "widgets", "status"));
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                id = "dev.long.widget",
                version = "1.0.0",
                name = "Widget Plugin",
                runtime = "webview",
                min_api_version = "1.1.0",
                entry_point = "index.html",
                capabilities = Array.Empty<string>(),
                widgets = new object[]
                {
                    new
                    {
                        id = "system.status",
                        title = "System Status",
                        entry_point = "widgets/status/index.html",
                        icon = "assets/status.png",
                        default_size = new { columns = 4, rows = 2 },
                    },
                },
            }));
        File.WriteAllText(Path.Combine(directory, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(directory, "assets", "status.png"), "png");
        if (includeWidgetEntry)
        {
            File.WriteAllText(
                Path.Combine(directory, "widgets", "status", "index.html"),
                "<html></html>");
        }

        return directory;
    }

    private string CreatePackageWithFileManifest(
        bool tamperEntryHash = false,
        string ledgerPluginId = "dev.long.test")
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.lpak");
        var manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            id = "dev.long.test",
            version = "1.0.0",
            name = "Test Plugin",
            author = "Long",
            runtime = "webview",
            entry_point = "index.html",
            capabilities = new[] { "storage.local" },
        }));
        var entryBytes = Encoding.UTF8.GetBytes("<html>test</html>");
        var ledgerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema_version = 1,
            classification = "long_plugin_file_manifest",
            plugin_id = ledgerPluginId,
            version = "1.0.0",
            files = new object[]
            {
                new
                {
                    path = "index.html",
                    size = entryBytes.LongLength,
                    sha256 = tamperEntryHash
                        ? new string('0', 64)
                        : Convert.ToHexString(SHA256.HashData(entryBytes)),
                },
                new
                {
                    path = "manifest.json",
                    size = manifestBytes.LongLength,
                    sha256 = Convert.ToHexString(SHA256.HashData(manifestBytes)),
                },
            },
        }));

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntryBytes(archive, "index.html", entryBytes);
        WriteEntryBytes(archive, "manifest.json", manifestBytes);
        WriteEntryBytes(archive, "package-files.json", ledgerBytes);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntryBytes(
        ZipArchive archive,
        string name,
        byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
