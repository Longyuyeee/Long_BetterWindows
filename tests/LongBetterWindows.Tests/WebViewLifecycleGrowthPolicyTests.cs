using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class WebViewLifecycleGrowthPolicyTests
{
    [Fact]
    public void Evaluate_AcceptsStableOrReleasedResources()
    {
        var result = WebViewLifecycleGrowthPolicy.Evaluate(
            1_000,
            1_024,
            34,
            32,
            300 * 1024 * 1024L,
            210 * 1024 * 1024L);

        Assert.True(result.Passed);
        Assert.Equal(24, result.HandleCount);
        Assert.Equal(-2, result.ThreadCount);
        Assert.True(result.PrivateMemoryBytes < 0);
    }

    [Theory]
    [InlineData(65, 0, 0)]
    [InlineData(0, 9, 0)]
    [InlineData(0, 0, 67_108_865)]
    public void Evaluate_RejectsGrowthBeyondAnyLimit(
        int handleGrowth,
        int threadGrowth,
        long privateMemoryGrowth)
    {
        var result = WebViewLifecycleGrowthPolicy.Evaluate(
            1_000,
            1_000 + handleGrowth,
            30,
            30 + threadGrowth,
            200 * 1024 * 1024L,
            200 * 1024 * 1024L + privateMemoryGrowth);

        Assert.False(result.Passed);
    }
}
