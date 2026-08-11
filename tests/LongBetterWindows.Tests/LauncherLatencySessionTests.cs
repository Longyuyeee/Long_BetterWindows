using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class LauncherLatencySessionTests
{
    [Fact]
    public void RecordsFirstFrameAndFirstActionableResultOnlyOnce()
    {
        long timestamp = 100;
        var session = new LauncherLatencySession(
            () => timestamp,
            (start, end) => TimeSpan.FromMilliseconds(end - start));

        session.BeginInvocation();
        timestamp = 118;
        Assert.Equal(TimeSpan.FromMilliseconds(18), session.MarkFirstFrame());
        timestamp = 125;
        Assert.Null(session.MarkFirstActionableResults(0));
        timestamp = 132;
        var result = session.MarkFirstActionableResults(3);
        timestamp = 180;

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromMilliseconds(32), result.InvocationElapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(32), result.QueryElapsed);
        Assert.Null(session.MarkFirstFrame());
        Assert.Null(session.MarkFirstActionableResults(4));
        Assert.Equal(
            "first_frame_ms=18.0;first_results_ms=32.0;query_first_results_ms=32.0",
            session.ToAutomationStatus());
    }

    [Fact]
    public void NewQueryKeepsInvocationMilestonesAndResetsQueryResult()
    {
        long timestamp = 10;
        var session = new LauncherLatencySession(
            () => timestamp,
            (start, end) => TimeSpan.FromMilliseconds(end - start));
        session.BeginInvocation();
        timestamp = 20;
        session.MarkFirstFrame();
        timestamp = 30;
        session.MarkFirstActionableResults(1);

        timestamp = 40;
        session.BeginQuery();
        Assert.Contains("query_first_results_ms=pending", session.ToAutomationStatus());
        timestamp = 47;
        var result = session.MarkFirstActionableResults(2);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromMilliseconds(20), result.InvocationElapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(7), result.QueryElapsed);
        Assert.Contains("first_frame_ms=10.0", session.ToAutomationStatus());
        Assert.Contains("first_results_ms=20.0", session.ToAutomationStatus());
    }
}
