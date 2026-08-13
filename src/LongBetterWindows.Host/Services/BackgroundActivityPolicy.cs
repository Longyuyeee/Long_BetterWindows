namespace LongBetterWindows.Host.Services;

internal static class BackgroundActivityPolicy
{
    internal const double MaximumHiddenCpuCorePercent = 6;
    internal const int MaximumHiddenWindowMessages = 20;

    internal static bool Evaluate(
        string pluginId,
        double hiddenCpuCorePercent,
        int hiddenWindowMessages,
        int hiddenApiCalls,
        bool hiddenHostState,
        bool restoredHostState)
        => hiddenCpuCorePercent <= MaximumHiddenCpuCorePercent
            && hiddenWindowMessages <= MaximumHiddenWindowMessages
            && hiddenApiCalls == 0
            && hiddenHostState
            && restoredHostState
            && pluginId is "com.long.clipboardhistory"
                or "com.long.hardwaremonitor"
                or "com.long.portmanager";
}
