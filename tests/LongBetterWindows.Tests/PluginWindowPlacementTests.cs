using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class PluginWindowPlacementTests
{
    [Fact]
    public void Calculate_CentersDipSizeOnNegativeCoordinateMonitor()
    {
        var workArea = new NativeWindowRect(-1920, 0, 0, 1040);

        var bounds = PluginWindowPlacement.Calculate(
            workArea,
            widthDip: 720,
            heightDip: 560,
            dpi: 120,
            fullScreen: false);

        Assert.Equal(new NativeWindowRect(-1410, 170, -510, 870), bounds);
    }

    [Fact]
    public void Calculate_FullScreenUsesExactTargetWorkArea()
    {
        var workArea = new NativeWindowRect(1280, 3, 2816, 819);

        var bounds = PluginWindowPlacement.Calculate(
            workArea,
            widthDip: 720,
            heightDip: 560,
            dpi: 144,
            fullScreen: true);

        Assert.Equal(workArea, bounds);
    }

    [Fact]
    public void Calculate_ClampsOversizedWindowToTargetWorkArea()
    {
        var workArea = new NativeWindowRect(1280, 3, 2816, 819);

        var bounds = PluginWindowPlacement.Calculate(
            workArea,
            widthDip: 2000,
            heightDip: 1200,
            dpi: 144,
            fullScreen: false);

        Assert.Equal(workArea, bounds);
    }
}
