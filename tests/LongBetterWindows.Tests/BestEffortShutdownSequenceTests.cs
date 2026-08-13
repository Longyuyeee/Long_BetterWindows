using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class BestEffortShutdownSequenceTests
{
    [Fact]
    public async Task RunAsync_ContinuesInOrderAndReportsFailures()
    {
        var executed = new List<string>();
        var expected = new InvalidOperationException("synthetic failure");
        var steps = new[]
        {
            BestEffortShutdownSequence.Sync(
                "first",
                () => executed.Add("first")),
            BestEffortShutdownSequence.Sync(
                "failing",
                () => throw expected),
            BestEffortShutdownSequence.Sync(
                "last",
                () => executed.Add("last")),
        };

        var failures = await BestEffortShutdownSequence.RunAsync(steps);

        Assert.Equal(["first", "last"], executed);
        var failure = Assert.Single(failures);
        Assert.Equal("failing", failure.Name);
        Assert.Same(expected, failure.Exception);
    }

    [Fact]
    public async Task RunAsync_AwaitsAsynchronousStepsBeforeContinuing()
    {
        var executed = new List<string>();
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var run = BestEffortShutdownSequence.RunAsync(
        [
            new ShutdownStep("async", async () =>
            {
                await gate.Task;
                executed.Add("async");
            }),
            BestEffortShutdownSequence.Sync(
                "next",
                () => executed.Add("next")),
        ]);

        Assert.Empty(executed);
        gate.SetResult();
        Assert.Empty(await run);
        Assert.Equal(["async", "next"], executed);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndContinuesWithNextStep()
    {
        var never = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continued = false;

        var failures = await BestEffortShutdownSequence.RunAsync(
        [
            new ShutdownStep(
                "stalled",
                () => new ValueTask(never.Task),
                TimeSpan.FromMilliseconds(30)),
            BestEffortShutdownSequence.Sync(
                "next",
                () => continued = true),
        ]);

        Assert.True(continued);
        var failure = Assert.Single(failures);
        Assert.Equal("stalled", failure.Name);
        Assert.IsType<TimeoutException>(failure.Exception);
    }
}
