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
}
