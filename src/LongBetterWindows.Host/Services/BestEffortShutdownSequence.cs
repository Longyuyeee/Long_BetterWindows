using Serilog;

namespace LongBetterWindows.Host.Services;

internal sealed record ShutdownStep(
    string Name,
    Func<ValueTask> ExecuteAsync,
    TimeSpan? Timeout = null);

internal sealed record ShutdownStepFailure(
    string Name,
    Exception Exception);

internal static class BestEffortShutdownSequence
{
    internal static async Task<IReadOnlyList<ShutdownStepFailure>> RunAsync(
        IEnumerable<ShutdownStep> steps)
    {
        var failures = new List<ShutdownStepFailure>();
        foreach (var step in steps)
        {
            try
            {
                var operation = step.ExecuteAsync();
                if (step.Timeout is { } timeout)
                {
                    if (timeout <= TimeSpan.Zero)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(step.Timeout),
                            "Shutdown step timeout must be positive.");
                    }
                    var operationTask = operation.AsTask();
                    try
                    {
                        await operationTask.WaitAsync(timeout)
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        _ = ObserveLateOperationAsync(
                            operationTask,
                            step.Name);
                        throw;
                    }
                }
                else
                {
                    await operation.ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failures.Add(new ShutdownStepFailure(step.Name, exception));
                Log.Error(
                    exception,
                    "Host shutdown step failed: {StepName}",
                    step.Name);
            }
        }
        return failures;
    }

    private static async Task ObserveLateOperationAsync(
        Task operation,
        string stepName)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Host shutdown step failed after timeout: {StepName}",
                stepName);
        }
    }

    internal static ShutdownStep Sync(string name, Action action)
        => new(name, () =>
        {
            action();
            return ValueTask.CompletedTask;
        });
}
