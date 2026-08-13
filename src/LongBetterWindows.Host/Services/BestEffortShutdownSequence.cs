using System.Diagnostics;
using Serilog;

namespace LongBetterWindows.Host.Services;

internal sealed record ShutdownStep(
    string Name,
    Func<ValueTask> ExecuteAsync,
    TimeSpan? Timeout = null);

internal enum ShutdownStepStatus
{
    Passed,
    Failed,
    TimedOut,
}

internal enum ShutdownErrorCategory
{
    None,
    OperationFailed,
    InvalidTimeout,
    Timeout,
}

internal sealed record ShutdownLateCompletion(
    ShutdownStepStatus Status,
    double ElapsedMilliseconds,
    ShutdownErrorCategory ErrorCategory);

internal sealed record ShutdownStepResult(
    string Name,
    ShutdownStepStatus Status,
    double ElapsedMilliseconds,
    double? TimeoutMilliseconds,
    ShutdownErrorCategory ErrorCategory,
    Exception? Exception = null,
    Task<ShutdownLateCompletion>? LateCompletion = null);

internal static class BestEffortShutdownSequence
{
    internal static async Task<IReadOnlyList<ShutdownStepResult>> RunAsync(
        IEnumerable<ShutdownStep> steps)
    {
        var results = new List<ShutdownStepResult>();
        foreach (var step in steps)
        {
            var stopwatch = Stopwatch.StartNew();
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
                    catch (TimeoutException exception)
                    {
                        var lateCompletion = ObserveLateOperationAsync(
                            operationTask,
                            step.Name,
                            stopwatch);
                        results.Add(new ShutdownStepResult(
                            step.Name,
                            ShutdownStepStatus.TimedOut,
                            stopwatch.Elapsed.TotalMilliseconds,
                            timeout.TotalMilliseconds,
                            ShutdownErrorCategory.Timeout,
                            exception,
                            lateCompletion));
                        Log.Error(
                            exception,
                            "Host shutdown step timed out: {StepName} after {ElapsedMs} ms",
                            step.Name,
                            stopwatch.Elapsed.TotalMilliseconds);
                        continue;
                    }
                }
                else
                {
                    await operation.ConfigureAwait(false);
                }

                results.Add(new ShutdownStepResult(
                    step.Name,
                    ShutdownStepStatus.Passed,
                    stopwatch.Elapsed.TotalMilliseconds,
                    step.Timeout?.TotalMilliseconds,
                    ShutdownErrorCategory.None));
            }
            catch (Exception exception)
            {
                var category = exception is ArgumentOutOfRangeException
                    ? ShutdownErrorCategory.InvalidTimeout
                    : ShutdownErrorCategory.OperationFailed;
                results.Add(new ShutdownStepResult(
                    step.Name,
                    ShutdownStepStatus.Failed,
                    stopwatch.Elapsed.TotalMilliseconds,
                    step.Timeout?.TotalMilliseconds,
                    category,
                    exception));
                Log.Error(
                    exception,
                    "Host shutdown step failed: {StepName} after {ElapsedMs} ms ({ErrorCategory})",
                    step.Name,
                    stopwatch.Elapsed.TotalMilliseconds,
                    category);
            }
        }

        return results;
    }

    private static async Task<ShutdownLateCompletion> ObserveLateOperationAsync(
        Task operation,
        string stepName,
        Stopwatch stopwatch)
    {
        try
        {
            await operation.ConfigureAwait(false);
            var completion = new ShutdownLateCompletion(
                ShutdownStepStatus.Passed,
                stopwatch.Elapsed.TotalMilliseconds,
                ShutdownErrorCategory.None);
            Log.Information(
                "Host shutdown step completed after timeout: {StepName} after {ElapsedMs} ms ({LateStatus})",
                stepName,
                completion.ElapsedMilliseconds,
                completion.Status);
            return completion;
        }
        catch (Exception exception)
        {
            var completion = new ShutdownLateCompletion(
                ShutdownStepStatus.Failed,
                stopwatch.Elapsed.TotalMilliseconds,
                ShutdownErrorCategory.OperationFailed);
            Log.Error(
                exception,
                "Host shutdown step failed after timeout: {StepName} after {ElapsedMs} ms ({LateStatus})",
                stepName,
                completion.ElapsedMilliseconds,
                completion.Status);
            return completion;
        }
    }

    internal static ShutdownStep Sync(string name, Action action)
        => new(name, () =>
        {
            action();
            return ValueTask.CompletedTask;
        });
}
