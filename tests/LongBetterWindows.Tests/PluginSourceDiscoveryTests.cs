using System.IO;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginSourceDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"long-plugin-sources-{Guid.NewGuid():N}");

    [Fact]
    public void ExplicitDirectory_IsCreatedAndRemainsTheOnlyScanRoot()
    {
        var pluginsDirectory = Path.Combine(_root, "isolated");

        var discovery = new PluginSourceDiscovery(pluginsDirectory);

        Assert.True(Directory.Exists(pluginsDirectory));
        Assert.Equal(Path.GetFullPath(pluginsDirectory),
            Assert.Single(discovery.ScanDirectories));
    }

    [Fact]
    public void Discover_ReturnsStableDirectoriesAndTopLevelScriptsOnly()
    {
        Directory.CreateDirectory(_root);
        var pluginB = Directory.CreateDirectory(Path.Combine(_root, "PluginB")).FullName;
        var pluginA = Directory.CreateDirectory(Path.Combine(_root, "PluginA")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, ".long_temp_preview"));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        File.WriteAllText(Path.Combine(_root, "alpha.csx"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "beta.JS"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "gamma.TS"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), string.Empty);
        File.WriteAllText(Path.Combine(nested, "ignored.js"), string.Empty);
        var discovery = new PluginSourceDiscovery(_root);

        var snapshot = discovery.Discover();

        Assert.Equal(
            new[] { nested, pluginA, pluginB }
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            snapshot.PluginDirectories);
        Assert.Equal(
            new[]
            {
                Path.Combine(_root, "alpha.csx"),
                Path.Combine(_root, "beta.JS"),
                Path.Combine(_root, "gamma.TS"),
            },
            snapshot.StandaloneScripts);
    }

    [Fact]
    public void FileClassification_ExcludesGeneratedTreesAndNestedStandaloneScripts()
    {
        Directory.CreateDirectory(_root);
        var nested = Directory.CreateDirectory(Path.Combine(_root, "PluginA")).FullName;
        var discovery = new PluginSourceDiscovery(_root);
        var rootScript = Path.Combine(_root, "tool.ts");
        var nestedScript = Path.Combine(nested, "tool.ts");

        Assert.True(discovery.IsStandaloneScript(rootScript));
        Assert.False(discovery.IsStandaloneScript(nestedScript));
        Assert.True(PluginSourceDiscovery.IsPluginFile(Path.Combine(nested, "MANIFEST.JSON")));
        Assert.False(PluginSourceDiscovery.IsPluginFile(Path.Combine(
            _root, ".long_temp_tool", "index.js")));
    }

    [Fact]
    public void PluginRootLookup_StopsAfterThreeParentLevels()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "manifest.json"), "{}");
        var withinLimit = Path.Combine(_root, "one", "two", "plugin.dll");
        var beyondLimit = Path.Combine(_root, "one", "two", "three", "plugin.dll");

        Assert.Equal(_root, PluginSourceDiscovery.FindPluginRootDirectory(withinLimit));
        Assert.Null(PluginSourceDiscovery.FindPluginRootDirectory(beyondLimit));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
