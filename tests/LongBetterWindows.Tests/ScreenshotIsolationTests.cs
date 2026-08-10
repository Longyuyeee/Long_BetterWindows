using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Contracts;
using ScreenshotPlugin;

namespace LongBetterWindows.Tests;

public sealed class ScreenshotIsolationTests
{
    [Fact]
    public void RegionGeometry_NormalizesPhysicalPointsOnNegativeMonitor()
    {
        var created = ScreenshotRegionGeometry.TryCreate(
            new Int32Rect(-1920, -200, 3840, 1280),
            new ScreenshotPhysicalPoint(-10, 60),
            new ScreenshotPhysicalPoint(-100, 50),
            out var region);

        Assert.True(created);
        Assert.Equal(new Int32Rect(-100, 50, 91, 11), region);
    }

    [Fact]
    public void RegionGeometry_RejectsSmallOrOutsidePhysicalSelection()
    {
        var virtualScreen = new Int32Rect(-1920, 0, 3840, 1080);

        Assert.False(ScreenshotRegionGeometry.TryCreate(
            virtualScreen,
            new ScreenshotPhysicalPoint(100, 100),
            new ScreenshotPhysicalPoint(104, 104),
            out _));
        Assert.False(ScreenshotRegionGeometry.TryCreate(
            virtualScreen,
            new ScreenshotPhysicalPoint(-1921, 100),
            new ScreenshotPhysicalPoint(-1900, 120),
            out _));
        Assert.False(ScreenshotRegionGeometry.TryCreate(
            virtualScreen,
            new ScreenshotPhysicalPoint(1900, 1000),
            new ScreenshotPhysicalPoint(1920, 1080),
            out _));
    }

    [Fact]
    public void OperationCoordinator_AllowsOnlyOneActiveScreenshotSession()
    {
        var coordinator = new ScreenshotOperationCoordinator();
        var first = coordinator.TryBegin();

        Assert.NotNull(first);
        Assert.True(coordinator.IsActive);
        Assert.Null(coordinator.TryBegin());

        first.Dispose();
        first.Dispose();
        using var second = coordinator.TryBegin();

        Assert.NotNull(second);
        Assert.True(coordinator.IsActive);
    }

    [Fact]
    public async Task ClipboardWriter_RetriesWithinSingleImageDelivery()
    {
        var bitmap = CreateBitmap();
        var writer = new ScreenshotClipboardWriter(
            [TimeSpan.Zero, TimeSpan.Zero]);
        var attempts = 0;
        var writes = new List<BitmapSource>();

        await writer.WriteAsync(
            bitmap,
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
            });

        Assert.Equal(3, attempts);
        Assert.Equal([bitmap], writes);
    }

    [Fact]
    public async Task ClipboardWriter_CancellationStopsFurtherAttempts()
    {
        var writer = new ScreenshotClipboardWriter(
            [TimeSpan.FromMilliseconds(10)]);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.WriteAsync(
                CreateBitmap(),
                _ =>
                {
                    attempts++;
                    cancellation.Cancel();
                    return Task.FromResult(HostApiResponse.Failure(
                        ApiErrorCode.Unknown,
                        "Clipboard is temporarily unavailable."));
                },
                cancellation.Token));

        Assert.Equal(1, attempts);
    }

    private static BitmapSource CreateBitmap()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[4],
            4);
        bitmap.Freeze();
        return bitmap;
    }
}
