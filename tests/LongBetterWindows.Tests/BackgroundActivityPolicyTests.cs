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
            [
                new(1, 3, 0), new(2, 4, 0), new(1.5, 2, 0),
                new(1, 1, 0), new(2, 3, 0), new(1.5, 2, 0),
            ],
            new WebViewLifecycleGrowthResult(3, 1, 1024, true),
            new CombinedResourceTrendResult(true, 1, 1, 1),
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
            [
                new(1, 0, 0), new(cpu, messages, apiCalls),
                new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0),
            ],
            new WebViewLifecycleGrowthResult(0, 0, 0, true),
            new CombinedResourceTrendResult(true, 0, 0, 0),
            allHostsHidden: true,
            cleanupPassed: true));

    [Fact]
    public void EvaluateCombinedResourceTrend_AcceptsNonMonotonicFluctuation()
    {
        var result = BackgroundActivityPolicy.EvaluateCombinedResourceTrend(
        [
            new(100, 20, 1_000),
            new(103, 21, 1_100),
            new(101, 20, 1_050),
            new(104, 21, 1_150),
            new(102, 20, 1_100),
            new(105, 21, 1_200),
        ]);

        Assert.True(result.Passed);
        Assert.Equal(1, result.HandleConsecutiveIncreases);
        Assert.Equal(1, result.ThreadConsecutiveIncreases);
        Assert.Equal(1, result.PrivateMemoryConsecutiveIncreases);
    }

    [Fact]
    public void EvaluateCombinedIdle_RejectsSustainedResourceTrend()
        => Assert.False(BackgroundActivityPolicy.EvaluateCombinedIdle(
            Enumerable.Repeat(
                new CombinedIdleSampleAssessment(1, 0, 0),
                BackgroundActivityPolicy.CombinedIdleSampleCount).ToArray(),
            new WebViewLifecycleGrowthResult(1, 1, 1024, true),
            new CombinedResourceTrendResult(false, 4, 0, 0),
            allHostsHidden: true,
            cleanupPassed: true));

    [Theory]
    [InlineData("handles")]
    [InlineData("threads")]
    [InlineData("memory")]
    public void EvaluateCombinedResourceTrend_RejectsSustainedIncrease(
        string resource)
    {
        var samples = Enumerable.Range(0, 6)
            .Select(index => new CombinedResourceSampleAssessment(
                resource == "handles" ? 100 + index : 100,
                resource == "threads" ? 20 + index : 20,
                resource == "memory" ? 1_000 + index : 1_000))
            .ToArray();

        var result = BackgroundActivityPolicy.EvaluateCombinedResourceTrend(
            samples);

        Assert.False(result.Passed);
    }

    [Fact]
    public void EvaluateCombinedGrowth_AllowsBoundedProcessGroupFluctuation()
        => Assert.True(BackgroundActivityPolicy.EvaluateCombinedGrowth(
            5000, 5096, 250, 282, 800_000_000, 867_108_864).Passed);

    [Theory]
    [InlineData(5097, 250, 800_000_000)]
    [InlineData(5000, 283, 800_000_000)]
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
