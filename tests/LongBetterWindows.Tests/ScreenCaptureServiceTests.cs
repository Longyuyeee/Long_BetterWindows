using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ScreenCaptureServiceTests
{
    [Fact]
    public async Task CaptureToBitmapAsync_CapturesPhysicalVirtualScreen()
    {
        var clipboard = new RecordingClipboardService();
        var service = new ScreenCaptureService(clipboard);
        var bounds = ScreenCaptureService.GetVirtualScreenBounds();

        var capture = await service.CaptureToBitmapAsync();
        var fullScreen = await service.CaptureFullScreenAsync();

        Assert.True(capture.IsSuccess, capture.ErrorMessage);
        Assert.NotNull(capture.Data);
        Assert.Equal(bounds.Width, capture.Data.PixelWidth);
        Assert.Equal(bounds.Height, capture.Data.PixelHeight);
        Assert.True(capture.Data.IsFrozen);
        Assert.True(fullScreen.IsSuccess, fullScreen.ErrorMessage);
        Assert.NotNull(clipboard.Image);
        Assert.Equal(bounds.Width, clipboard.Image.PixelWidth);
        Assert.Equal(bounds.Height, clipboard.Image.PixelHeight);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public async Task CaptureRegionAsync_RejectsInvalidDimensions(
        int width,
        int height)
    {
        var service = new ScreenCaptureService(new RecordingClipboardService());

        var result = await service.CaptureRegionAsync(0, 0, width, height);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task CaptureRegionAsync_RejectsAreaOutsideVirtualScreen()
    {
        var service = new ScreenCaptureService(new RecordingClipboardService());
        var bounds = ScreenCaptureService.GetVirtualScreenBounds();

        var result = await service.CaptureRegionAsync(
            bounds.X + bounds.Width,
            bounds.Y + bounds.Height,
            1,
            1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public void ScreenColorSampler_SamplesPhysicalScreenPixel()
    {
        var bounds = ScreenCaptureService.GetVirtualScreenBounds();

        var sampler = new ScreenColorSampler();
        var result = sampler.Sample(
            bounds.X + (bounds.Width / 2),
            bounds.Y + (bounds.Height / 2));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.Matches("^#[0-9A-F]{6}$", result.Data.Hex);
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged
        {
            add { }
            remove { }
        }

        public bool IsMonitoring => false;
        public BitmapSource? Image { get; private set; }

        public Task<HostApiResponse<string?>> GetTextAsync()
            => Task.FromResult(HostApiResponse<string?>.Success(null));

        public Task<HostApiResponse> SetTextAsync(string text)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> SetImageAsync(BitmapSource image)
        {
            Image = image;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<BitmapSource?>> GetImageAsync()
            => Task.FromResult(
                HostApiResponse<BitmapSource?>.Success(Image));

        public Task<HostApiResponse> ClearAsync()
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> StartMonitoringAsync()
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> StopMonitoringAsync()
            => Task.FromResult(HostApiResponse.Success());
    }
}
