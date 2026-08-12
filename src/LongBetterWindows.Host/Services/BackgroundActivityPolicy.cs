namespace LongBetterWindows.Host.Services;

internal static class BackgroundActivityPolicy
{
    internal const double MaximumHiddenCpuCorePercent = 5;
    internal const int MaximumHiddenWindowMessages = 20;

    internal static bool Evaluate(
        string pluginId,
        double hiddenCpuCorePercent,
        int hiddenWindowMessages,
        int hiddenPerformanceCalls,
        int hiddenClipboardReads,
        bool hiddenHostState,
        bool restoredHostState)
        => hiddenCpuCorePercent <= MaximumHiddenCpuCorePercent
            && hiddenWindowMessages <= MaximumHiddenWindowMessages
            && hiddenPerformanceCalls == 0
            && hiddenClipboardReads == 0
            && hiddenHostState
            && restoredHostState
            && pluginId is "com.long.clipboardhistory"
                or "com.long.hardwaremonitor";
}
