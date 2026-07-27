using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public enum WindowLayout
    {
        Left,
        Right,
        Maximize,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        ThirdLeft,
        ThirdRight,
    }

    public enum WindowDisplayState
    {
        Normal,
        Minimized,
        Maximized,
    }

    public class WindowInfo
    {
        public string Title { get; init; } = "";
        public string ProcessName { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public bool IsTopmost { get; init; }
        public WindowDisplayState DisplayState { get; init; }
    }

    public sealed class WindowOperationOutcome
    {
        public bool Changed { get; init; }
        public bool RecoveryAttempted { get; init; }
        public bool RecoverySucceeded { get; init; }
        public ApiErrorCode RecoveryErrorCode { get; init; }
        public string? RecoveryErrorMessage { get; init; }
        public WindowInfo? Before { get; init; }
        public WindowInfo? After { get; init; }
    }

    public interface IWindowInfoService
    {
        Task<HostApiResponse<WindowInfo>> GetForegroundWindowInfoAsync();
        Task<HostApiResponse<List<WindowInfo>>> GetVisibleWindowsAsync();
        HostApiResponse<WindowOperationOutcome> ApplyForegroundLayout(
            WindowLayout layout);
        HostApiResponse<WindowOperationOutcome> ToggleForegroundTopmost();
    }
}
