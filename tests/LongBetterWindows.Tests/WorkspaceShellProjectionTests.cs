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
    public void ManagementCatalog_MapsAllEightPagesToUniqueStableModules()
    {
        var pages = Enum.GetValues<WorkspaceManagementPage>();

        var modules = pages
            .Select(page => WorkspaceManagementModuleCatalog.Create(
                page,
                key => $"localized:{key}"))
            .ToArray();

        Assert.Equal(8, modules.Select(module => module.Key).Distinct().Count());
        Assert.False(modules[0].CanClose);
        Assert.All(modules.Skip(1), module => Assert.True(module.CanClose));
        Assert.All(modules, module => Assert.StartsWith("localized:", module.Title));
        foreach (var (page, module) in pages.Zip(modules))
        {
            Assert.True(
                WorkspaceManagementModuleCatalog.TryResolvePage(
                    module.Key,
                    out var resolved));
            Assert.Equal(page, resolved);
        }
    }

    [Fact]
    public void ManagementCatalog_RejectsUnsupportedModuleKey()
    {
        Assert.False(WorkspaceManagementModuleCatalog.TryResolvePage(
            new WorkspaceModuleKey("unknown", "unknown"),
            out _));
    }

    [Fact]
    public void ManagementModules_ReopenWithoutDuplicationAndCloseToMostRecent()
    {
        var root = Management(WorkspaceManagementPage.Overview);
        var market = Management(WorkspaceManagementPage.Market);
        var settings = Management(WorkspaceManagementPage.Settings);
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

    [Theory]
    [InlineData("management", "root", "management")]
    [InlineData("marketplace", "catalog", "marketplace")]
    [InlineData("management-page", "plugins", "installed-plugins")]
    public void SearchScopeCatalog_MapsSupportedWorkspaceModules(
        string kind,
        string resourceId,
        string expectedScope)
    {
        var scope = WorkspaceSearchScopeCatalog.Resolve(
            new WorkspaceModuleKey(kind, resourceId));

        Assert.NotNull(scope);
        Assert.Equal(expectedScope, scope.ScopeId);
        Assert.StartsWith("workspace.search.", scope.PlaceholderResourceKey);
    }

    [Fact]
    public void SearchScopeCatalog_LeavesUnsupportedModulesWithoutFakeSearch()
    {
        Assert.Null(WorkspaceSearchScopeCatalog.Resolve(
            new WorkspaceModuleKey("settings", "root")));
        Assert.Null(WorkspaceSearchScopeCatalog.Resolve(
            new WorkspaceModuleKey("workflow", "sample")));
    }

    private static WorkspaceModuleDescriptor Management(
        WorkspaceManagementPage page)
        => WorkspaceManagementModuleCatalog.Create(page);

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
