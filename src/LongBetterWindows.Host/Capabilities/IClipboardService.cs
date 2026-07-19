using LongBetterWindows.Host.Contracts;
using System.Windows.Media.Imaging;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IClipboardService
    {
        Task<HostApiResponse<string?>> GetTextAsync();
        Task<HostApiResponse<BitmapSource?>> GetImageAsync();
        Task<HostApiResponse> SetTextAsync(string text);
        Task<HostApiResponse> SetImageAsync(BitmapSource image);
        Task<HostApiResponse> ClearAsync();
    }
}
