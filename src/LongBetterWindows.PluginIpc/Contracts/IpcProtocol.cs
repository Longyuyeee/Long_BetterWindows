namespace LongBetterWindows.PluginIpc.Contracts;

public static class IpcProtocol
{
    public const string Name = "long.plugin.ipc/1.0";
    public const int MaximumFrameBytes = 1024 * 1024;
    public const int DefaultDeadlineMilliseconds = 10_000;
    public const int MinimumDeadlineMilliseconds = 100;
    public const int MaximumDeadlineMilliseconds = 120_000;

    public static int NormalizeDeadline(int? deadlineMilliseconds)
    {
        var value = deadlineMilliseconds ?? DefaultDeadlineMilliseconds;
        if (value < MinimumDeadlineMilliseconds || value > MaximumDeadlineMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadlineMilliseconds),
                $"Deadline must be between {MinimumDeadlineMilliseconds} and {MaximumDeadlineMilliseconds} milliseconds.");
        }

        return value;
    }
}
