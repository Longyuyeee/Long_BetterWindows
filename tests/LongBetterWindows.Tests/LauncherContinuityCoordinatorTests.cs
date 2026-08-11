using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class LauncherContinuityCoordinatorTests
{
    [Fact]
    public void MatchingWorkspaceTarget_ConsumesIntentExactlyOnce()
    {
        var coordinator = new LauncherContinuityCoordinator();
        coordinator.Begin(
            "marketplace:catalog",
            Intent("market", (nint)42));

        Assert.True(coordinator.HasPendingFor("MARKETPLACE:CATALOG"));
        Assert.Null(coordinator.TryConsume("management:root", true));

        var state = coordinator.TryConsume("marketplace:catalog", true);

        Assert.NotNull(state);
        Assert.Equal("market", state.Query);
        Assert.Equal((nint)42, state.OriginWindowHandle);
        Assert.False(coordinator.HasPendingFor("marketplace:catalog"));
        Assert.Null(coordinator.TryConsume("marketplace:catalog", true));
    }

    [Fact]
    public void ReplacingOrCancellingTransition_ClearsSensitivePayload()
    {
        var first = Intent("first", (nint)1);
        var second = Intent("second", (nint)2);
        var coordinator = new LauncherContinuityCoordinator();

        coordinator.Begin("management:root", first);
        coordinator.Begin("settings:root", second);

        Assert.True(first.IsConsumed);
        Assert.Equal(string.Empty, first.Query);
        coordinator.Cancel("settings:root");
        Assert.True(second.IsConsumed);
        Assert.False(coordinator.HasPendingFor("settings:root"));
    }

    [Fact]
    public void SuperPanelTransition_PreservesOriginSurfaceAndContext()
    {
        var context = new ContextSnapshot(
            DateTimeOffset.UtcNow,
            [new ContextItem
            {
                Id = "selection",
                Source = ContextSource.ExplorerSelection,
                Label = "Selected text",
                Text = "value",
                CompatibleInputTypes = [AcceptedInputType.Text],
            }]);
        var coordinator = new LauncherContinuityCoordinator();
        coordinator.Begin(
            "management:root",
            new LauncherReturnIntent(
                (nint)84,
                string.Empty,
                context,
                LauncherReturnMode.RestoreLauncher,
                DateTimeOffset.UtcNow,
                LauncherSurface.SuperPanel));

        var state = coordinator.TryConsume("management:root", true);

        Assert.NotNull(state);
        Assert.Equal(LauncherSurface.SuperPanel, state.Surface);
        Assert.Equal((nint)84, state.OriginWindowHandle);
        Assert.Same(context, state.Context);
    }

    private static LauncherReturnIntent Intent(string query, nint origin)
        => new(
            origin,
            query,
            ContextSnapshot.Empty,
            LauncherReturnMode.RestoreLauncher,
            DateTimeOffset.UtcNow);
}
