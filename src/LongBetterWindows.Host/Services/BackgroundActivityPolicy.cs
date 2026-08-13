namespace LongBetterWindows.Host.Services;

internal static class BackgroundActivityPolicy
{
    internal const double MaximumHiddenCpuCorePercent = 6;
    internal const int MaximumHiddenWindowMessages = 20;
    internal const int CombinedIdleSampleCount = 6;
    internal const int CombinedIdleSampleMilliseconds = 12_000;
    internal const int MaximumConsecutiveResourceIncreaseIntervals = 3;
    internal const int MaximumCombinedWindowMessages = 30;
    internal const int MaximumCombinedHandleGrowth = 96;
    internal const int MaximumCombinedThreadGrowth = 32;
    internal const long MaximumCombinedPrivateMemoryGrowthBytes =
        64L * 1024 * 1024;
    internal const int MixedPresentationCycleCount = 4;

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
        CombinedResourceTrendResult resourceTrend,
        bool allHostsHidden,
        bool cleanupPassed)
        => samples.Count == CombinedIdleSampleCount
            && samples.All(sample =>
                sample.CpuCorePercent <= MaximumHiddenCpuCorePercent
                && sample.WindowMessages <= MaximumCombinedWindowMessages
                && sample.ApiCalls == 0)
            && growth.Passed
            && resourceTrend.Passed
            && allHostsHidden
            && cleanupPassed;

    internal static CombinedResourceTrendResult EvaluateCombinedResourceTrend(
        IReadOnlyList<CombinedResourceSampleAssessment> samples)
    {
        var handles = MaximumConsecutiveIncreases(
            samples.Select(sample => (long)sample.HandleCount));
        var threads = MaximumConsecutiveIncreases(
            samples.Select(sample => (long)sample.ThreadCount));
        var privateMemory = MaximumConsecutiveIncreases(
            samples.Select(sample => sample.PrivateMemoryBytes));
        return new CombinedResourceTrendResult(
            handles <= MaximumConsecutiveResourceIncreaseIntervals
                && threads <= MaximumConsecutiveResourceIncreaseIntervals
                && privateMemory <= MaximumConsecutiveResourceIncreaseIntervals,
            handles,
            threads,
            privateMemory);
    }

    private static int MaximumConsecutiveIncreases(IEnumerable<long> values)
    {
        long? previous = null;
        var current = 0;
        var maximum = 0;
        foreach (var value in values)
        {
            if (previous is not null && value > previous.Value)
            {
                current++;
                maximum = Math.Max(maximum, current);
            }
            else
            {
                current = 0;
            }
            previous = value;
        }
        return maximum;
    }

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

internal sealed record CombinedResourceSampleAssessment(
    int HandleCount,
    int ThreadCount,
    long PrivateMemoryBytes);

internal sealed record CombinedResourceTrendResult(
    bool Passed,
    int HandleConsecutiveIncreases,
    int ThreadConsecutiveIncreases,
    int PrivateMemoryConsecutiveIncreases);
