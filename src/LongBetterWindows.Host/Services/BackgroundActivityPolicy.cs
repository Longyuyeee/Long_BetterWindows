namespace LongBetterWindows.Host.Services;

internal static class BackgroundActivityPolicy
{
    internal const double MaximumHiddenCpuCorePercent = 6;
    internal const int MaximumHiddenWindowMessages = 20;
    internal const int CombinedIdleSampleCount = 3;
    internal const int CombinedIdleSampleMilliseconds = 8_000;
    internal const int MaximumCombinedWindowMessages = 30;
    internal const int MaximumCombinedHandleGrowth = 96;
    internal const int MaximumCombinedThreadGrowth = 24;
    internal const long MaximumCombinedPrivateMemoryGrowthBytes =
        64L * 1024 * 1024;

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

    internal static bool EvaluateCombinedIdle(
        IReadOnlyList<CombinedIdleSampleAssessment> samples,
        WebViewLifecycleGrowthResult growth,
        bool allHostsHidden,
        bool cleanupPassed)
        => samples.Count == CombinedIdleSampleCount
            && samples.All(sample =>
                sample.CpuCorePercent <= MaximumHiddenCpuCorePercent
                && sample.WindowMessages <= MaximumCombinedWindowMessages
                && sample.ApiCalls == 0)
            && growth.Passed
            && allHostsHidden
            && cleanupPassed;

    internal static WebViewLifecycleGrowthResult EvaluateCombinedGrowth(
        int warmHandleCount,
        int finalHandleCount,
        int warmThreadCount,
        int finalThreadCount,
        long warmPrivateMemoryBytes,
        long finalPrivateMemoryBytes)
    {
        var handles = finalHandleCount - warmHandleCount;
        var threads = finalThreadCount - warmThreadCount;
        var privateMemory = finalPrivateMemoryBytes - warmPrivateMemoryBytes;
        return new WebViewLifecycleGrowthResult(
            handles,
            threads,
            privateMemory,
            handles <= MaximumCombinedHandleGrowth
                && threads <= MaximumCombinedThreadGrowth
                && privateMemory <= MaximumCombinedPrivateMemoryGrowthBytes);
    }
}

internal sealed record CombinedIdleSampleAssessment(
    double CpuCorePercent,
    int WindowMessages,
    int ApiCalls);
