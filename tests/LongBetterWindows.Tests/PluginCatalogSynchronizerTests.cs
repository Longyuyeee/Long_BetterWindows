using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginCatalogGenerator;

namespace LongBetterWindows.Tests;

public sealed class PluginCatalogSynchronizerTests : IDisposable
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"long-plugin-catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task GenerateAsync_IsDeterministicAndMatchesCommittedOutputs()
    {
        var synchronizer = new PluginCatalogSynchronizer();

        var first = await synchronizer.GenerateAsync(RepositoryRoot);
        var second = await synchronizer.GenerateAsync(RepositoryRoot);

        Assert.Equal(first.RegistryJson, second.RegistryJson);
        Assert.Equal(first.DocumentationMarkdown, second.DocumentationMarkdown);
        Assert.Equal(
            Normalize(first.RegistryJson),
            Normalize(File.ReadAllText(Path.Combine(
                RepositoryRoot,
                PluginCatalogSynchronizer.RegistryRelativePath))));
        Assert.Equal(
            Normalize(first.DocumentationMarkdown),
            Normalize(File.ReadAllText(Path.Combine(
                RepositoryRoot,
                PluginCatalogSynchronizer.DocumentationRelativePath))));
    }

    [Fact]
    public async Task SynchronizeAsync_CheckModeAcceptsCurrentOutputsWithoutWriting()
    {
        var result = await new PluginCatalogSynchronizer().SynchronizeAsync(
            RepositoryRoot,
            checkOnly: true);

        Assert.Empty(result.ChangedPaths);
        Assert.Equal(2, result.OutputPaths.Count);
    }

    [Fact]
    public async Task GeneratedRegistry_LoadsAsEightLocalEntriesWithoutReleaseMetadata()
    {
        Directory.CreateDirectory(_tempDirectory);
        var generated = await new PluginCatalogSynchronizer().GenerateAsync(RepositoryRoot);
        var path = Path.Combine(_tempDirectory, "registry.json");
        await File.WriteAllTextAsync(path, generated.RegistryJson);

        var result = await new LocalMarketplaceRepository(path).LoadAsync();

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(8, result.Catalog!.Entries.Count);
        Assert.All(result.Catalog.Entries, entry =>
        {
            Assert.Equal(LongBetterWindows.Host.Contracts.MarketplaceSourceKind.LocalPackage, entry.Source);
            var version = Assert.Single(entry.Versions);
            Assert.False(string.IsNullOrWhiteSpace(version.Version));
            Assert.Equal(default, version.PublishedAt);
            Assert.Null(version.PackageUri);
            Assert.Empty(version.ReleaseNotes);
            Assert.Empty(version.Sha256);
        });
        var screenshot = Assert.Single(
            result.Catalog.Entries,
            entry => entry.Id == "com.long.screenshot");
        Assert.Equal("1.2.0", screenshot.Versions[0].Version);
        Assert.Contains("system.screenshot", screenshot.Versions[0].Capabilities);

        using var document = JsonDocument.Parse(generated.RegistryJson);
        Assert.False(document.RootElement.TryGetProperty("generated_at", out _));
        Assert.All(
            document.RootElement.GetProperty("entries").EnumerateArray(),
            entry => Assert.All(
                entry.GetProperty("versions").EnumerateArray(),
                version =>
                {
                    Assert.False(version.TryGetProperty("published_at", out _));
                    Assert.False(version.TryGetProperty("release_notes", out _));
                    Assert.False(version.TryGetProperty("package_uri", out _));
                    Assert.False(version.TryGetProperty("sha256", out _));
                }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
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
}
