using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkspaceShellProjectionTests
{
    [Fact]
    public void Build_PreservesModuleOrderAndProjectsActiveAndCloseState()
    {
        var root = Module("management", "root", "Management", canClose: false);
        var market = Module("marketplace", "catalog", "Market");
        var settings = Module("settings", "root", "Settings");
        var coordinator = new WorkspaceSessionCoordinator(root);
        coordinator.Open(market);
        coordinator.Open(settings);

        var tabs = WorkspaceShellProjection.Build(coordinator.State);

        Assert.Equal(
            new[] { root.Key, market.Key, settings.Key },
            tabs.Select(tab => tab.Key));
        Assert.False(tabs[0].CanClose);
        Assert.True(tabs[2].IsActive);
        Assert.Single(tabs, tab => tab.IsActive);
    }

    [Fact]
    public void Build_UsesTitleSelectorWithoutChangingNavigationIdentity()
    {
        var root = Module("management", "root", "Original", canClose: false);
        var coordinator = new WorkspaceSessionCoordinator(root);

        var tabs = WorkspaceShellProjection.Build(
            coordinator.State,
            module => $"Localized {module.Key.ResourceId}");

        var tab = Assert.Single(tabs);
        Assert.Equal(root.Key, tab.Key);
        Assert.Equal("Localized root", tab.Title);
    }

    [Fact]
    public void LegacyCatalog_MapsAllEightPagesToUniqueStableModules()
    {
        var pages = new[]
        {
            "overview",
            "workflows",
            "plugins",
            "market",
            "system",
            "diagnostics",
            "developer",
            "settings",
        };

        var modules = pages.Select(page =>
        {
            Assert.True(WorkspaceLegacyModuleCatalog.TryCreate(
                page,
                key => $"localized:{key}",
                out var module));
            return module!;
        }).ToArray();

        Assert.Equal(8, modules.Select(module => module.Key).Distinct().Count());
        Assert.False(modules[0].CanClose);
        Assert.All(modules.Skip(1), module => Assert.True(module.CanClose));
        Assert.All(modules, module => Assert.StartsWith("localized:", module.Title));
    }

    [Fact]
    public void LegacyCatalog_RejectsUnknownPage()
    {
        Assert.False(WorkspaceLegacyModuleCatalog.TryCreate(
            "unknown",
            null,
            out var module));
        Assert.Null(module);
    }

    [Fact]
    public void LegacyModules_ReopenWithoutDuplicationAndCloseToMostRecent()
    {
        var root = Legacy("overview");
        var market = Legacy("market");
        var settings = Legacy("settings");
        var coordinator = new WorkspaceSessionCoordinator(root);
        coordinator.Open(market);
        coordinator.Open(settings);
        coordinator.Open(market);

        var reopened = coordinator.Open(market);
        var closed = coordinator.Close(market.Key);

        Assert.Equal(WorkspaceNavigationChangeKind.None, reopened.Kind);
        Assert.Equal(3, reopened.State.Modules.Count);
        Assert.Equal(settings.Key, closed.State.ActiveModuleKey);
    }

    private static WorkspaceModuleDescriptor Legacy(string page)
    {
        Assert.True(WorkspaceLegacyModuleCatalog.TryCreate(
            page,
            null,
            out var module));
        return module!;
    }

    private static WorkspaceModuleDescriptor Module(
        string kind,
        string resourceId,
        string title,
        bool canClose = true)
        => new(
            new WorkspaceModuleKey(kind, resourceId),
            title,
            canClose);
}
