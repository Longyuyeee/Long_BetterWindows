using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IScreenCaptureService
    {
        Task<HostApiResponse> CaptureFullScreenAsync();
        Task<HostApiResponse<BitmapSource>> CaptureToBitmapAsync();
    }
}
