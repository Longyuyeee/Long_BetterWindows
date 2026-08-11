using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class QualityMonitorPlacementTests
{
    [Fact]
    public void Select_MatchesDeviceNameWithoutCaseSensitivity()
    {
        var monitors = new[]
        {
            Monitor(@"\\.\DISPLAY1", primary: true),
            Monitor(@"\\.\DISPLAY2", primary: false),
        };

        var selected = QualityMonitorPlacement.Select(
            monitors,
            @"\\.\display2");

        Assert.Equal(@"\\.\DISPLAY2", selected?.DeviceName);
        Assert.False(selected?.IsPrimary);
    }

    [Fact]
    public void Select_RejectsUnknownDevice()
    {
        var selected = QualityMonitorPlacement.Select(
            new[] { Monitor(@"\\.\DISPLAY1", primary: true) },
            @"\\.\DISPLAY9");

        Assert.Null(selected);
    }

    private static QualityMonitorDescriptor Monitor(
        string name,
        bool primary)
        => new(
            IntPtr.Zero,
            name,
            new NativeWindowRect(0, 0, 1920, 1080),
            new NativeWindowRect(0, 0, 1920, 1040),
            primary,
            96);
}
