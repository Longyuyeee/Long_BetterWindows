using System.Windows;
using System.Windows.Interop;

namespace LongBetterWindows.Host.Services;

internal sealed class WindowMessageActivityTrace : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, int> _counts = new();
    private readonly List<WindowMessageCheckpoint> _checkpoints = new();
    private bool _disposed;

    public WindowMessageActivityTrace(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "Quality window message trace requires an initialized handle.");
        _source.AddHook(WindowProc);
    }

    public IReadOnlyList<WindowMessageCheckpoint> Checkpoints
        => _checkpoints;

    public void Mark(string stage)
    {
        if (_disposed)
            return;

        _checkpoints.Add(new WindowMessageCheckpoint(
            stage,
            _counts.Values.Sum(),
            _counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(12)
                .Select(pair => new WindowMessageCount(
                    pair.Key,
                    GetMessageName(pair.Key),
                    pair.Value))
                .ToArray()));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.RemoveHook(WindowProc);
    }

    internal static string GetMessageName(int message)
        => message switch
        {
            0x000F => "WM_PAINT",
            0x0020 => "WM_SETCURSOR",
            0x003D => "WM_GETOBJECT",
            0x0046 => "WM_WINDOWPOSCHANGING",
            0x0047 => "WM_WINDOWPOSCHANGED",
            0x0084 => "WM_NCHITTEST",
            0x0113 => "WM_TIMER",
            0x0200 => "WM_MOUSEMOVE",
            0x02A3 => "WM_MOUSELEAVE",
            0x031A => "WM_THEMECHANGED",
            0x031E => "WM_DWMCOMPOSITIONCHANGED",
            _ => $"0x{message:X4}",
        };

    private IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        _counts.TryGetValue(message, out var count);
        _counts[message] = count + 1;
        return IntPtr.Zero;
    }
}

internal sealed record WindowMessageCheckpoint(
    string Stage,
    int TotalCount,
    IReadOnlyList<WindowMessageCount> TopMessages);

internal sealed record WindowMessageCount(
    int Id,
    string Name,
    int Count);
