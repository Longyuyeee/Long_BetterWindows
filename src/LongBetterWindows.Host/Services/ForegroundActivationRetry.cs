namespace LongBetterWindows.Host.Services;

internal static class ForegroundActivationRetry
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(240),
    ];

    internal static async Task<bool> RunAsync(
        Func<bool> tryActivate,
        Func<bool> canContinue,
        CancellationToken cancellationToken = default)
    {
        if (tryActivate())
            return true;

        try
        {
            foreach (var delay in RetryDelays)
            {
                await Task.Delay(delay, cancellationToken);
                if (!canContinue())
                    return false;
                if (tryActivate())
                    return true;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return false;
    }
}
