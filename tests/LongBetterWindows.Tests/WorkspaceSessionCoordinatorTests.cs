using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class WorkspaceSessionCoordinatorTests
{
    [Fact]
    public void Constructor_StartsWithProtectedRootModule()
    {
        var session = CreateSession();

        var state = session.State;

        Assert.Equal(Key("management", "root"), state.ActiveModuleKey);
        Assert.Equal("管理中心", state.ActiveModule.Title);
        Assert.Single(state.Modules);
        Assert.False(state.ActiveModule.CanClose);
    }

    [Fact]
    public void Descriptor_RejectsDefaultModuleKey()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new WorkspaceModuleDescriptor(default, "Invalid"));

        Assert.Equal("key", error.ParamName);
    }

    [Fact]
    public void Open_DeduplicatesCanonicalKeyAndActivatesExistingModule()
    {
        var session = CreateSession();
        var market = Module("Marketplace", "Catalog", "插件市场");
        var settings = Module("settings", "root", "设置");
        session.Open(market);
        session.Open(settings);
        var changes = 0;
        session.StateChanged += (_, _) => changes++;

        var result = session.Open(Module(" marketplace ", " CATALOG ", "重复市场"));

        Assert.Equal(WorkspaceNavigationChangeKind.Activated, result.Kind);
        Assert.Equal(market.Key, result.State.ActiveModuleKey);
        Assert.Equal(3, result.State.Modules.Count);
        Assert.Equal("插件市场", result.State.ActiveModule.Title);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Close_ActiveModuleReturnsToMostRecentlyUsedValidModule()
    {
        var session = CreateSession();
        var market = Module("marketplace", "catalog", "插件市场");
        var settings = Module("settings", "root", "设置");
        session.Open(market);
        session.Open(settings);
        session.Activate(market.Key);

        var result = session.Close(market.Key);

        Assert.Equal(WorkspaceNavigationChangeKind.Closed, result.Kind);
        Assert.Equal(settings.Key, result.State.ActiveModuleKey);
        Assert.False(result.State.Contains(market.Key));
    }

    [Fact]
    public void Close_InactiveModuleKeepsCurrentModuleActive()
    {
        var session = CreateSession();
        var market = Module("marketplace", "catalog", "插件市场");
        var settings = Module("settings", "root", "设置");
        session.Open(market);
        session.Open(settings);

        var result = session.Close(market.Key);

        Assert.Equal(settings.Key, result.State.ActiveModuleKey);
        Assert.False(result.State.Contains(market.Key));
    }

    [Fact]
    public void Close_RootIsProtectedAndDoesNotRaiseChange()
    {
        var session = CreateSession();
        var changes = 0;
        session.StateChanged += (_, _) => changes++;

        var result = session.Close(Key("management", "root"));

        Assert.Equal(WorkspaceNavigationChangeKind.Protected, result.Kind);
        Assert.False(result.Changed);
        Assert.Single(result.State.Modules);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Open_FactoryFailureLeavesStateUntouched()
    {
        var session = CreateSession();
        var before = session.State;
        var changes = 0;
        session.StateChanged += (_, _) => changes++;

        Assert.Throws<InvalidOperationException>(() =>
            session.Open(() => throw new InvalidOperationException("creation failed")));

        var after = session.State;
        Assert.Equal(before.ActiveModuleKey, after.ActiveModuleKey);
        Assert.Equal(
            before.Modules.Select(module => module.Key),
            after.Modules.Select(module => module.Key));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RemoveModules_IsAtomicDeduplicatedAndProtectsRoot()
    {
        var session = CreateSession();
        var pluginSettings = Module(
            "plugin-settings",
            "com.long.fixture",
            "Fixture 设置");
        var pluginRuntime = Module(
            "plugin-runtime",
            "com.long.fixture",
            "Fixture",
            instanceId: "session-1");
        var market = Module("marketplace", "catalog", "插件市场");
        session.Open(market);
        session.Open(pluginSettings);
        session.Open(pluginRuntime);
        var changes = new List<WorkspaceNavigationChangedEventArgs>();
        session.StateChanged += (_, change) => changes.Add(change);

        var result = session.RemoveModules(
        [
            Key("management", "root"),
            pluginSettings.Key,
            pluginRuntime.Key,
            pluginRuntime.Key,
        ]);

        Assert.Equal(WorkspaceNavigationChangeKind.Removed, result.Kind);
        Assert.Equal(2, result.AffectedCount);
        Assert.Equal(market.Key, result.State.ActiveModuleKey);
        Assert.True(result.State.Contains(Key("management", "root")));
        Assert.False(result.State.Contains(pluginSettings.Key));
        Assert.False(result.State.Contains(pluginRuntime.Key));
        var change = Assert.Single(changes);
        Assert.Equal(2, change.AffectedCount);
        Assert.Equal(result.State.ActiveModuleKey, change.Current.ActiveModuleKey);
    }

    [Fact]
    public void Activate_MissingOrCurrentModuleDoesNotRaiseChange()
    {
        var session = CreateSession();
        var changes = 0;
        session.StateChanged += (_, _) => changes++;

        var current = session.Activate(Key("management", "root"));
        var missing = session.Activate(Key("settings", "missing"));

        Assert.Equal(WorkspaceNavigationChangeKind.None, current.Kind);
        Assert.Equal(WorkspaceNavigationChangeKind.NotFound, missing.Kind);
        Assert.Equal(0, changes);
    }

    private static WorkspaceSessionCoordinator CreateSession()
        => new(new WorkspaceModuleDescriptor(
            Key("management", "root"),
            "管理中心",
            canClose: false,
            searchScopeId: "management"));

    private static WorkspaceModuleDescriptor Module(
        string kind,
        string resourceId,
        string title,
        string? instanceId = null)
        => new(
            Key(kind, resourceId, instanceId),
            title,
            canClose: true);

    private static WorkspaceModuleKey Key(
        string kind,
        string resourceId,
        string? instanceId = null)
        => new(kind, resourceId, instanceId);
}
