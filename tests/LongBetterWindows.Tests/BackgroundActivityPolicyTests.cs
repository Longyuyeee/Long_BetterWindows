using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class BackgroundActivityPolicyTests
{
    [Fact]
    public void Evaluate_AcceptsSilentHiddenPluginThatRestores()
        => Assert.True(BackgroundActivityPolicy.Evaluate(
            "com.long.hardwaremonitor", 1, 3, 0, 0, true, true));

    [Theory]
    [InlineData(6, 0, 0, 0, true, true)]
    [InlineData(0, 21, 0, 0, true, true)]
    [InlineData(0, 0, 1, 0, true, true)]
    [InlineData(0, 0, 0, 1, true, true)]
    [InlineData(0, 0, 0, 0, false, true)]
    [InlineData(0, 0, 0, 0, true, false)]
    public void Evaluate_RejectsHiddenActivityOrBrokenVisibility(
        double cpu,
        int messages,
        int performanceCalls,
        int clipboardReads,
        bool hidden,
        bool restored)
        => Assert.False(BackgroundActivityPolicy.Evaluate(
            "com.long.hardwaremonitor",
            cpu, messages, performanceCalls, clipboardReads, hidden, restored));
}
