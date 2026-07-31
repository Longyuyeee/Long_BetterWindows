using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class WorkspaceLauncherSearchProviderTests
{
    [Fact]
    public async Task EmptyQuery_ExposesCoreDestinationsWithoutPluginFlood()
    {
        var registry = new PluginRegistry();
        Register(registry, "sample", "Sample");
        var provider = new WorkspaceLauncherSearchProvider(registry);

        var results = await provider.SearchAsync(new SearchRequest(
            string.Empty,
            ContextSnapshot.Empty,
            20));

        Assert.Equal(
            [
                "workspace:management:root",
                "workspace:marketplace:catalog",
                "workspace:widgets:root",
                "workspace:settings:root",
            ],
            results.Select(item => item.Id));
        Assert.All(results, item => Assert.Equal(
            SearchActionKind.OpenWorkspaceModule,
            item.PrimaryAction.Kind));
        Assert.DoesNotContain(
            results,
            item => item.Id.Contains("plugin-settings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PluginQuery_ProjectsSettingsTargetWithoutActivatingPlugin()
    {
        var registry = new PluginRegistry();
        registry.RegisterDeferred(
            Manifest("sample", "Sample Plugin"),
            "/sample",
            _ => throw new InvalidOperationException("Search must stay read-only."));
        var provider = new WorkspaceLauncherSearchProvider(registry);

        var results = await provider.SearchAsync(new SearchRequest(
            "sample",
            ContextSnapshot.Empty,
            20));

        var result = Assert.Single(results);
        Assert.Equal("workspace:plugin-settings:sample", result.Id);
        Assert.Equal("plugin-settings:sample", result.PrimaryAction.Target);
        Assert.False(registry.Get("sample")!.IsActivated);
    }

    [Fact]
    public async Task PreferredPluginSettings_RemainsAvailableInEmptyQuery()
    {
        var registry = new PluginRegistry();
        Register(registry, "sample", "Sample");
        var provider = new WorkspaceLauncherSearchProvider(registry);

        var results = await provider.SearchAsync(new SearchRequest(
            string.Empty,
            ContextSnapshot.Empty,
            20,
            RecentResultIds: ["workspace:plugin-settings:sample"]));

        Assert.Contains(
            results,
            item => item.Id == "workspace:plugin-settings:sample");
    }

    private static void Register(
        PluginRegistry registry,
        string id,
        string name)
        => registry.Register(Manifest(id, name), new object(), null, $"/{id}");

    private static PluginManifest Manifest(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            EntryPoint = $"{id}.dll",
        };
}
