using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class PluginWorkspaceSessionTests
{
    [Fact]
    public void PresentationPolicy_OnlyWebViewRuntimeUsesHostWorkspaceSession()
    {
        Assert.Equal(
            PluginSurfaceOwnership.HostWorkspaceSession,
            PluginWorkspacePresentationPolicy.Resolve(new PluginManifest
            {
                Runtime = "webview",
            }));
        Assert.Equal(
            PluginSurfaceOwnership.PluginOwned,
            PluginWorkspacePresentationPolicy.Resolve(new PluginManifest()));
        Assert.Equal(
            PluginSurfaceOwnership.PluginOwned,
            PluginWorkspacePresentationPolicy.Resolve(new PluginManifest
            {
                Runtime = "csharp-script",
            }));
    }

    [Fact]
    public void PlacementTransitions_PreserveSessionAndLastVisiblePlacement()
    {
        var session = new PluginWorkspaceSession(
            "session-one",
            "plugin.one",
            PluginWorkspacePlacement.Embedded);

        var embedded = session.ShowEmbedded();
        var detached = session.ShowDetached();
        var hidden = session.Hide();

        Assert.True(embedded.Changed);
        Assert.True(detached.Changed);
        Assert.True(hidden.Changed);
        Assert.Equal("session-one", hidden.Current.SessionId);
        Assert.Equal("plugin.one", hidden.Current.PluginId);
        Assert.Equal(PluginWorkspacePlacement.Hidden, hidden.Current.Placement);
        Assert.Equal(
            PluginWorkspacePlacement.Detached,
            hidden.Current.LastVisiblePlacement);
        Assert.Equal(3, hidden.Current.Revision);
    }

    [Fact]
    public void EndedSession_RejectsFurtherPlacementChanges()
    {
        var session = new PluginWorkspaceSession(
            "session",
            "plugin",
            PluginWorkspacePlacement.Detached);
        session.ShowDetached();

        var ended = session.End();
        var attempted = session.ShowEmbedded();

        Assert.True(ended.Changed);
        Assert.True(ended.Current.IsEnded);
        Assert.False(attempted.Changed);
        Assert.Equal(ended.Current, attempted.Current);
    }

    [Fact]
    public void Manager_ReusesActiveSessionAndCreatesNewAfterEnd()
    {
        var ids = new Queue<string>(["one", "two"]);
        var manager = new PluginWorkspaceSessionManager(ids.Dequeue);

        var first = manager.GetOrCreate(
            "plugin",
            PluginWorkspacePlacement.Embedded);
        var reused = manager.GetOrCreate(
            "PLUGIN",
            PluginWorkspacePlacement.Detached);
        Assert.Same(first, reused);

        Assert.True(manager.End(first.State.SessionId));
        var second = manager.GetOrCreate(
            "plugin",
            PluginWorkspacePlacement.Detached);

        Assert.NotSame(first, second);
        Assert.Equal("two", second.State.SessionId);
        Assert.Null(manager.GetBySessionId("one"));
    }

    [Fact]
    public void Manager_FindsActiveSessionByPluginId()
    {
        var manager = new PluginWorkspaceSessionManager(() => "session_1");
        var session = manager.GetOrCreate(
            "plugin.one",
            PluginWorkspacePlacement.Detached);

        Assert.Same(session, manager.GetByPluginId("PLUGIN.ONE"));
        Assert.True(manager.End(session.State.SessionId));
        Assert.Null(manager.GetByPluginId("plugin.one"));
    }

    [Fact]
    public void Manager_RejectsDuplicateGeneratedSessionIds()
    {
        var manager = new PluginWorkspaceSessionManager(() => "same");
        manager.GetOrCreate("first", PluginWorkspacePlacement.Embedded);

        Assert.Throws<InvalidOperationException>(() =>
            manager.GetOrCreate("second", PluginWorkspacePlacement.Embedded));
    }

    [Theory]
    [InlineData(true, true, true, (int)PluginSurfaceCloseAction.Ignore)]
    [InlineData(false, true, true, (int)PluginSurfaceCloseAction.ReturnToEmbedded)]
    [InlineData(false, true, false, (int)PluginSurfaceCloseAction.HideAndApplyLifecycle)]
    [InlineData(false, false, true, (int)PluginSurfaceCloseAction.HideAndApplyLifecycle)]
    public void CloseRouter_DistinguishesReturnFromLifecycleClose(
        bool closingForStop,
        bool returnRequested,
        bool canEmbed,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)PluginSurfaceCloseRouter.Route(
                closingForStop,
                returnRequested,
                canEmbed));
    }
}
