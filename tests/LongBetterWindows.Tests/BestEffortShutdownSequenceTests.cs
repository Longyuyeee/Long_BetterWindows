using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class BestEffortShutdownSequenceTests
{
    [Fact]
    public async Task RunAsync_ContinuesInOrderAndReportsEveryStep()
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

        var results = await BestEffortShutdownSequence.RunAsync(steps);

        Assert.Equal(["first", "last"], executed);
        Assert.Equal(3, results.Count);
        Assert.Equal(
            [ShutdownStepStatus.Passed, ShutdownStepStatus.Failed, ShutdownStepStatus.Passed],
            results.Select(result => result.Status));
        var failure = results[1];
        Assert.Equal("failing", failure.Name);
        Assert.Equal(ShutdownErrorCategory.OperationFailed, failure.ErrorCategory);
        Assert.Same(expected, failure.Exception);
        Assert.All(results, result => Assert.True(result.ElapsedMilliseconds >= 0));
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
        var results = await run;
        Assert.All(results, result =>
            Assert.Equal(ShutdownStepStatus.Passed, result.Status));
        Assert.Equal(["async", "next"], executed);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndContinuesWithNextStep()
    {
        var never = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continued = false;

        var results = await BestEffortShutdownSequence.RunAsync(
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
        Assert.Equal(2, results.Count);
        var timeout = results[0];
        Assert.Equal("stalled", timeout.Name);
        Assert.Equal(ShutdownStepStatus.TimedOut, timeout.Status);
        Assert.Equal(ShutdownErrorCategory.Timeout, timeout.ErrorCategory);
        Assert.Equal(30, timeout.TimeoutMilliseconds);
        Assert.IsType<TimeoutException>(timeout.Exception);
        Assert.NotNull(timeout.LateCompletion);
        Assert.Equal(ShutdownStepStatus.Passed, results[1].Status);
        never.SetResult();
    }

    [Fact]
    public async Task RunAsync_RecordsLateSuccessWithoutChangingTimeoutDecision()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var results = await BestEffortShutdownSequence.RunAsync(
        [
            new ShutdownStep(
                "late-success",
                () => new ValueTask(gate.Task),
                TimeSpan.FromMilliseconds(30)),
        ]);

        var result = Assert.Single(results);
        gate.SetResult();
        var completion = await result.LateCompletion!.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.Equal(ShutdownStepStatus.TimedOut, result.Status);
        Assert.Equal(ShutdownStepStatus.Passed, completion.Status);
        Assert.Equal(ShutdownErrorCategory.None, completion.ErrorCategory);
        Assert.True(completion.ElapsedMilliseconds >= result.ElapsedMilliseconds);
    }

    [Fact]
    public async Task RunAsync_RecordsLateFailureWithoutExposingItsMessageInResult()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var results = await BestEffortShutdownSequence.RunAsync(
        [
            new ShutdownStep(
                "late-failure",
                () => new ValueTask(gate.Task),
                TimeSpan.FromMilliseconds(30)),
        ]);

        var result = Assert.Single(results);
        gate.SetException(new InvalidOperationException("sensitive detail"));
        var completion = await result.LateCompletion!.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.Equal(ShutdownStepStatus.TimedOut, result.Status);
        Assert.Equal(ShutdownStepStatus.Failed, completion.Status);
        Assert.Equal(
            ShutdownErrorCategory.OperationFailed,
            completion.ErrorCategory);
    }

    [Fact]
    public async Task RunAsync_ClassifiesInvalidTimeoutAndContinues()
    {
        var continued = false;

        var results = await BestEffortShutdownSequence.RunAsync(
        [
            new ShutdownStep(
                "invalid",
                () => ValueTask.CompletedTask,
                TimeSpan.Zero),
            BestEffortShutdownSequence.Sync(
                "next",
                () => continued = true),
        ]);

        Assert.True(continued);
        Assert.Equal(ShutdownStepStatus.Failed, results[0].Status);
        Assert.Equal(
            ShutdownErrorCategory.InvalidTimeout,
            results[0].ErrorCategory);
        Assert.Equal(ShutdownStepStatus.Passed, results[1].Status);
    }
}
