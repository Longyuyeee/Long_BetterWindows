using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ForegroundActivationRetryTests
{
    [Fact]
    public async Task RunAsync_RetriesTransientActivationFailure()
    {
        var attempts = 0;

        var activated = await ForegroundActivationRetry.RunAsync(
            () => ++attempts == 2,
            () => true);

        Assert.True(activated);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RunAsync_StopsWhenPanelBecomesVisibleAgain()
    {
        var attempts = 0;

        var activated = await ForegroundActivationRetry.RunAsync(
            () =>
            {
                attempts++;
                return false;
            },
            () => false);

        Assert.False(activated);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsPendingRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        cancellation.Cancel();

        var activated = await ForegroundActivationRetry.RunAsync(
            () =>
            {
                attempts++;
                return false;
            },
            () => true,
            cancellation.Token);

        Assert.False(activated);
        Assert.Equal(1, attempts);
    }
}
