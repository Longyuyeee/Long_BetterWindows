using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public class WindowInfo
    {
        public string Title { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public bool IsTopmost { get; init; }
    }

    public interface IWindowInfoService
    {
        Task<HostApiResponse<WindowInfo>> GetForegroundWindowInfoAsync();
        Task<HostApiResponse<List<WindowInfo>>> GetVisibleWindowsAsync();
    }
}
