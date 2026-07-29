using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Tests;

public sealed class AsyncDeliveryBoundaryTests
{
    [Fact]
    public async Task RunAsync_DeliversProducedPayloadOnce()
    {
        var delivered = new List<string>();

        var result = await AsyncDeliveryBoundary.RunAsync(
            () => Task.FromResult("payload"),
            value =>
            {
                delivered.Add(value);
                return Task.CompletedTask;
            });

        Assert.Equal("payload", result);
        Assert.Equal(["payload"], delivered);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeProduction_DoesNotProduceOrDeliver()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var produced = false;
        var delivered = false;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            AsyncDeliveryBoundary.RunAsync(
                () =>
                {
                    produced = true;
                    return Task.FromResult("payload");
                },
                _ =>
                {
                    delivered = true;
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.False(produced);
        Assert.False(delivered);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringProduction_DoesNotDeliver()
    {
        using var cancellation = new CancellationTokenSource();
        var productionStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var finishProduction =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = false;

        var operation = AsyncDeliveryBoundary.RunAsync(
            async () =>
            {
                productionStarted.SetResult();
                return await finishProduction.Task;
            },
            _ =>
            {
                delivered = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        await productionStarted.Task;
        cancellation.Cancel();
        finishProduction.SetResult("late-payload");

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.False(delivered);
    }
}
