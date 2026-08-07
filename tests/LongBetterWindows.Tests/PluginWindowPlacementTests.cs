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

    [Fact]
    public void Constrain_PreservesVisibleWindowAfterTaskbarMovesToLeft()
    {
        var bounds = new NativeWindowRect(0, 100, 900, 700);
        var workArea = new NativeWindowRect(80, 0, 1920, 1080);

        var constrained = PluginWindowPlacement.Constrain(bounds, workArea);

        Assert.Equal(new NativeWindowRect(80, 100, 980, 700), constrained);
    }

    [Fact]
    public void Constrain_RecoversWindowAfterSecondaryMonitorIsRemoved()
    {
        var bounds = new NativeWindowRect(3336, 141, 5236, 1841);
        var remainingWorkArea = new NativeWindowRect(0, 0, 2560, 1344);

        var constrained = PluginWindowPlacement.Constrain(
            bounds,
            remainingWorkArea);

        Assert.Equal(new NativeWindowRect(660, 0, 2560, 1344), constrained);
    }

    [Fact]
    public void Constrain_DoesNotMoveWindowAlreadyInsideWorkArea()
    {
        var bounds = new NativeWindowRect(-1500, 120, -500, 820);
        var workArea = new NativeWindowRect(-1920, 0, 0, 1040);

        var constrained = PluginWindowPlacement.Constrain(bounds, workArea);

        Assert.Equal(bounds, constrained);
    }

    [Fact]
    public void Constrain_IgnoresTransientInvalidWorkArea()
    {
        var bounds = new NativeWindowRect(200, 100, 1100, 700);

        var constrained = PluginWindowPlacement.Constrain(
            bounds,
            new NativeWindowRect(0, 0, 0, 1080));

        Assert.Equal(bounds, constrained);
    }
}
