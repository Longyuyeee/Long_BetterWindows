using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class PerformanceRefreshCoordinatorTests
{
    [Fact]
    public void Stop_ReleasesBackgroundPerformanceSampler()
    {
        var monitor = new PerformanceMonitorService();
        using var coordinator = new PerformanceRefreshCoordinator(monitor);

        coordinator.Start();
        Assert.True(monitor.IsMonitoring);

        coordinator.Stop();
        Assert.False(monitor.IsMonitoring);
    }
}
