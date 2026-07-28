using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class WorkspaceSearchSessionTests
{
    [Fact]
    public void Queries_AreIsolatedAndRestoredPerModule()
    {
        using var session = new WorkspaceSearchSession();
        var market = Key("marketplace", "catalog");
        var settings = Key("settings", "root");

        session.ActivateModule(market);
        session.SetQuery(market, "clipboard");
        session.ActivateModule(settings);
        session.SetQuery(settings, "theme");

        Assert.Equal("theme", session.GetQuery(settings));
        Assert.Equal("clipboard", session.ActivateModule(market));
        Assert.Equal(market, session.ActiveModuleKey);
    }

    [Fact]
    public void ActiveQueryChange_CancelsOldRequestButInactiveChangeDoesNot()
    {
        using var session = new WorkspaceSearchSession();
        var market = Key("marketplace", "catalog");
        var settings = Key("settings", "root");
        session.ActivateModule(market);
        session.SetQuery(market, "first");
        var request = session.BeginSearch();

        session.SetQuery(settings, "theme");
        Assert.False(request.CancellationToken.IsCancellationRequested);

        session.SetQuery(market, "second");
        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(session.IsCurrent(request));
    }

    [Fact]
    public void ModuleActivation_CancelsPreviousModuleRequest()
    {
        using var session = new WorkspaceSearchSession();
        var market = Key("marketplace", "catalog");
        session.ActivateModule(market);
        var request = session.BeginSearch();

        session.ActivateModule(Key("settings", "root"));

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(session.IsCurrent(request));
    }

    [Fact]
    public void BeginSearch_SupersedesPreviousRequestWithMonotonicRevision()
    {
        using var session = new WorkspaceSearchSession();
        session.ActivateModule(Key("marketplace", "catalog"));
        var first = session.BeginSearch();

        var second = session.BeginSearch();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.True(second.Revision > first.Revision);
        Assert.False(session.IsCurrent(first));
        Assert.True(session.IsCurrent(second));
        Assert.False(session.Complete(first));
        Assert.True(session.Complete(second));
        Assert.False(session.IsCurrent(second));
    }

    [Fact]
    public void RemoveActiveModule_CancelsRequestAndClearsActivation()
    {
        using var session = new WorkspaceSearchSession();
        var market = Key("marketplace", "catalog");
        session.ActivateModule(market);
        session.SetQuery(market, "query");
        var request = session.BeginSearch();

        var removed = session.RemoveModule(market);

        Assert.True(removed);
        Assert.Null(session.ActiveModuleKey);
        Assert.Equal(string.Empty, session.GetQuery(market));
        Assert.True(request.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void InvalidKeyAndMissingActivationAreRejected()
    {
        using var session = new WorkspaceSearchSession();

        Assert.Throws<ArgumentException>(() => session.ActivateModule(default));
        Assert.Throws<InvalidOperationException>(() => session.BeginSearch());
    }

    [Fact]
    public void Dispose_CancelsRequestAndLateCompletionIsIgnored()
    {
        var session = new WorkspaceSearchSession();
        session.ActivateModule(Key("marketplace", "catalog"));
        var request = session.BeginSearch();

        session.Dispose();

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(session.IsCurrent(request));
        Assert.False(session.Complete(request));
    }

    private static WorkspaceModuleKey Key(string kind, string resourceId)
        => new(kind, resourceId);
}
