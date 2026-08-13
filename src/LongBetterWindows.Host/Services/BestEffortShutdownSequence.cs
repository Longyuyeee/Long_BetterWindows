using Serilog;

namespace LongBetterWindows.Host.Services;

internal sealed record ShutdownStep(
    string Name,
    Func<ValueTask> ExecuteAsync);

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
                await step.ExecuteAsync().ConfigureAwait(false);
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

    internal static ShutdownStep Sync(string name, Action action)
        => new(name, () =>
        {
            action();
            return ValueTask.CompletedTask;
        });
}
