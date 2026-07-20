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

        /// <summary>监听剪贴板变化事件</summary>
        event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;

        /// <summary>开始监听剪贴板变化（需要 system.clipboard.monitor 能力）</summary>
        Task<HostApiResponse> StartMonitoringAsync();

        /// <summary>停止监听剪贴板变化</summary>
        Task<HostApiResponse> StopMonitoringAsync();

        /// <summary>检查是否正在监听</summary>
        bool IsMonitoring { get; }
    }

    public class ClipboardChangedEventArgs : EventArgs
    {
        public string? Text { get; set; }
        public ClipboardContentType ContentType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum ClipboardContentType
    {
        Text,
        Image,
        Files,
        Unknown
    }
}
