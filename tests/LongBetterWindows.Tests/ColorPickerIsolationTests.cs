using System.Windows;
using ColorPickerPlugin;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ColorPickerIsolationTests
{
    [Fact]
    public void PopupPlacement_StaysInsideNegativeCoordinateMonitorAndAvoidsCursor()
    {
        var workArea = new Int32Rect(-1920, -200, 1920, 1080);

        var placement = ColorPickerPopupPlacement.Calculate(
            workArea,
            cursorX: -10,
            cursorY: 850,
            popupWidth: 250,
            popupHeight: 214);

        Assert.True(placement.X >= workArea.X);
        Assert.True(placement.Y >= workArea.Y);
        Assert.True(placement.X + placement.Width <= workArea.X + workArea.Width);
        Assert.True(placement.Y + placement.Height <= workArea.Y + workArea.Height);
        Assert.False(
            -10 >= placement.X
            && -10 < placement.X + placement.Width
            && 850 >= placement.Y
            && 850 < placement.Y + placement.Height);
    }

    [Fact]
    public void PopupPlacement_PrefersPhysicalOffsetWhenSpaceIsAvailable()
    {
        var placement = ColorPickerPopupPlacement.Calculate(
            new Int32Rect(0, 0, 1920, 1080),
            cursorX: 100,
            cursorY: 120,
            popupWidth: 250,
            popupHeight: 214);

        Assert.Equal(new Int32Rect(118, 138, 250, 214), placement);
    }

    [Fact]
    public async Task DeliveryCoordinator_DeliversOnlyFirstSelection()
    {
        var coordinator = new ColorPickerDeliveryCoordinator();
        var writes = new List<string>();

        var first = await coordinator.TryDeliverAsync(
            "#12ABEF",
            value =>
            {
                writes.Add(value);
                return Task.CompletedTask;
            });
        var second = await coordinator.TryDeliverAsync(
            "#445566",
            value =>
            {
                writes.Add(value);
                return Task.CompletedTask;
            });

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(["#12ABEF"], writes);
    }

    [Fact]
    public async Task DeliveryCoordinator_CancelledSessionDoesNotDeliver()
    {
        var coordinator = new ColorPickerDeliveryCoordinator();
        var delivered = false;

        Assert.True(coordinator.Cancel());
        var result = await coordinator.TryDeliverAsync(
            "#123456",
            _ =>
            {
                delivered = true;
                return Task.CompletedTask;
            });

        Assert.False(result);
        Assert.False(delivered);
    }

    [Fact]
    public async Task ClipboardWriter_RetriesTransientFailureWithinSingleDelivery()
    {
        var coordinator = new ColorPickerDeliveryCoordinator();
        var writer = new ColorPickerClipboardWriter(
            [TimeSpan.Zero, TimeSpan.Zero]);
        var attempts = 0;
        var writes = new List<string>();

        var first = await coordinator.TryDeliverAsync(
            "#12ABEF",
            value => writer.WriteAsync(
                value,
                candidate =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        return Task.FromResult(HostApiResponse.Failure(
                            ApiErrorCode.Unknown,
                            "Clipboard is temporarily unavailable."));
                    }

                    writes.Add(candidate);
                    return Task.FromResult(HostApiResponse.Success());
                }));
        var second = await coordinator.TryDeliverAsync(
            "#445566",
            _ => Task.CompletedTask);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(3, attempts);
        Assert.Equal(["#12ABEF"], writes);
    }

    [Fact]
    public void ScreenColorSampler_RejectsCoordinateOutsideVirtualScreen()
    {
        var bounds = ScreenCaptureService.GetVirtualScreenBounds();
        var sampler = new ScreenColorSampler();

        var result = sampler.Sample(bounds.X + bounds.Width, bounds.Y);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
    }
}
