using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class BackgroundActivityPolicyTests
{
    [Fact]
    public void Evaluate_AcceptsSilentHiddenPluginThatRestores()
        => Assert.True(BackgroundActivityPolicy.Evaluate(
            "com.long.portmanager", 1, 3, 0, true, true));

    [Theory]
    [InlineData(6.1, 0, 0, true, true)]
    [InlineData(0, 21, 0, true, true)]
    [InlineData(0, 0, 1, true, true)]
    [InlineData(0, 0, 0, false, true)]
    [InlineData(0, 0, 0, true, false)]
    public void Evaluate_RejectsHiddenActivityOrBrokenVisibility(
        double cpu,
        int messages,
        int apiCalls,
        bool hidden,
        bool restored)
        => Assert.False(BackgroundActivityPolicy.Evaluate(
            "com.long.hardwaremonitor",
            cpu, messages, apiCalls, hidden, restored));

    [Fact]
    public void EvaluateCombinedIdle_AcceptsStableSilentSamples()
        => Assert.True(BackgroundActivityPolicy.EvaluateCombinedIdle(
            [new(1, 3, 0), new(2, 4, 0), new(1.5, 2, 0)],
            new WebViewLifecycleGrowthResult(3, 1, 1024, true),
            allHostsHidden: true,
            cleanupPassed: true));

    [Theory]
    [InlineData(6.1, 0, 0)]
    [InlineData(0, 31, 0)]
    [InlineData(0, 0, 1)]
    public void EvaluateCombinedIdle_RejectsActiveSample(
        double cpu,
        int messages,
        int apiCalls)
        => Assert.False(BackgroundActivityPolicy.EvaluateCombinedIdle(
            [new(1, 0, 0), new(cpu, messages, apiCalls), new(1, 0, 0)],
            new WebViewLifecycleGrowthResult(0, 0, 0, true),
            allHostsHidden: true,
            cleanupPassed: true));

    [Fact]
    public void EvaluateCombinedGrowth_AllowsBoundedProcessGroupFluctuation()
        => Assert.True(BackgroundActivityPolicy.EvaluateCombinedGrowth(
            5000, 5096, 250, 274, 800_000_000, 867_108_864).Passed);

    [Theory]
    [InlineData(5097, 250, 800_000_000)]
    [InlineData(5000, 275, 800_000_000)]
    [InlineData(5000, 250, 867_108_865)]
    public void EvaluateCombinedGrowth_RejectsExcessiveProcessGroupGrowth(
        int finalHandles,
        int finalThreads,
        long finalPrivateMemory)
        => Assert.False(BackgroundActivityPolicy.EvaluateCombinedGrowth(
            5000,
            finalHandles,
            250,
            finalThreads,
            800_000_000,
            finalPrivateMemory).Passed);
}
