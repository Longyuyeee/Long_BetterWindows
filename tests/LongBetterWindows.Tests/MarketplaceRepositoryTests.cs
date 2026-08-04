using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-market-tests-{Guid.NewGuid():N}");

    public MarketplaceRepositoryTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task SearchAsync_UsesNameTagsAndCategory()
    {
        var path = await WriteCatalogAsync(new MarketplaceCatalog
        {
            Source = MarketplaceSourceKind.RemoteRegistry,
            Entries = new[]
            {
                Entry("dev.long.clipboard", "Clipboard Studio", "Productivity", "clipboard", "1.2.0"),
                Entry("dev.long.color", "Color Lab", "Design", "palette", "2.0.0"),
            },
        });
        var repository = new LocalMarketplaceRepository(path);

        var results = await repository.SearchAsync("clipboard", "Productivity");

        var item = Assert.Single(results);
        Assert.Equal("dev.long.clipboard", item.Id);
        Assert.Equal(MarketplaceSourceKind.LocalPackage, item.Source);
    }

    [Fact]
    public async Task LoadAsync_DuplicatePluginId_IsRejected()
    {
        var path = await WriteCatalogAsync(new MarketplaceCatalog
        {
            Entries = new[]
            {
                Entry("same.id", "First", "Tools", "one", "1.0.0"),
                Entry("same.id", "Second", "Tools", "two", "1.1.0"),
            },
        });

        var result = await new LocalMarketplaceRepository(path).LoadAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("重复插件 ID", result.Error);
    }

    [Theory]
    [InlineData(null, MarketplaceInstallState.NotInstalled)]
    [InlineData("1.0.0", MarketplaceInstallState.UpdateAvailable)]
    [InlineData("1.2.0", MarketplaceInstallState.Installed)]
    [InlineData("2.0.0", MarketplaceInstallState.DowngradeAvailable)]
    public void GetInstallState_ComparesInstalledWithLatest(
        string? installedVersion,
        MarketplaceInstallState expected)
    {
        var entry = Entry("dev.long.test", "Test", "Tools", "test", "1.2.0");

        Assert.Equal(expected, LocalMarketplaceRepository.GetInstallState(entry, installedVersion));
    }

    [Fact]
    public async Task BundledCatalog_LoadsWithUniqueEntriesAndVersionMetadata()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root, "src", "LongBetterWindows.Host", "Marketplace", "registry.json");

        var result = await new LocalMarketplaceRepository(path).LoadAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(8, result.Catalog!.Entries.Count);
        Assert.All(result.Catalog.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Category));
            Assert.NotEmpty(entry.Versions);
            Assert.All(entry.Versions, version =>
                Assert.False(string.IsNullOrWhiteSpace(version.Version)));
        });
    }

    private async Task<string> WriteCatalogAsync(MarketplaceCatalog catalog)
    {
        var path = Path.Combine(_tempDir, "registry.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(catalog));
        return path;
    }

    private static MarketplaceEntry Entry(
        string id, string name, string category, string tag, string version)
        => new()
        {
            Id = id,
            Name = name,
            Summary = $"{name} summary",
            Publisher = "Long Labs",
            Category = category,
            Tags = new[] { tag },
            Versions = new[] { new MarketplacePackageVersion { Version = version } },
        };

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
