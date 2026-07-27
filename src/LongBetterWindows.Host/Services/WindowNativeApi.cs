using System.Runtime.InteropServices;

namespace LongBetterWindows.Host.Services;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeWindowRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowPlacement
{
    public uint Length;
    public uint Flags;
    public uint ShowCommand;
    public NativePoint MinPosition;
    public NativePoint MaxPosition;
    public NativeWindowRect NormalPosition;
}

internal interface IWindowNativeApi
{
    IntPtr GetForegroundWindow();
    bool IsWindow(IntPtr window);
    bool TryGetWindowRect(
        IntPtr window,
        out NativeWindowRect rect,
        out int error);
    bool TryGetWindowPlacement(
        IntPtr window,
        out NativeWindowPlacement placement,
        out int error);
    bool TrySetWindowPlacement(
        IntPtr window,
        NativeWindowPlacement placement,
        out int error);
    bool TryGetTopmost(IntPtr window, out bool topmost, out int error);
    bool TrySetWindowPosition(
        IntPtr window,
        IntPtr insertAfter,
        NativeWindowRect rect,
        uint flags,
        out int error);
    IntPtr MonitorFromWindow(IntPtr window);
    bool TryGetMonitorWorkArea(
        IntPtr monitor,
        out NativeWindowRect workArea,
        out int error);
}

internal sealed class WindowNativeApi : IWindowNativeApi
{
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008L;
    private const uint MonitorDefaultToNearest = 2;

    public IntPtr GetForegroundWindow() => NativeGetForegroundWindow();

    public bool IsWindow(IntPtr window) => NativeIsWindow(window);

    public bool TryGetWindowRect(
        IntPtr window,
        out NativeWindowRect rect,
        out int error)
    {
        var succeeded = NativeGetWindowRect(window, out rect);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public bool TryGetWindowPlacement(
        IntPtr window,
        out NativeWindowPlacement placement,
        out int error)
    {
        placement = new NativeWindowPlacement
        {
            Length = (uint)Marshal.SizeOf<NativeWindowPlacement>(),
        };
        var succeeded = NativeGetWindowPlacement(window, ref placement);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public bool TrySetWindowPlacement(
        IntPtr window,
        NativeWindowPlacement placement,
        out int error)
    {
        placement.Length = (uint)Marshal.SizeOf<NativeWindowPlacement>();
        var succeeded = NativeSetWindowPlacement(window, ref placement);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public bool TryGetTopmost(
        IntPtr window,
        out bool topmost,
        out int error)
    {
        Marshal.SetLastPInvokeError(0);
        var style = NativeGetWindowLongPtr(window, GwlExStyle);
        error = Marshal.GetLastWin32Error();
        if (style == IntPtr.Zero && error != 0)
        {
            topmost = false;
            return false;
        }

        topmost = (style.ToInt64() & WsExTopmost) != 0;
        error = 0;
        return true;
    }

    public bool TrySetWindowPosition(
        IntPtr window,
        IntPtr insertAfter,
        NativeWindowRect rect,
        uint flags,
        out int error)
    {
        var succeeded = NativeSetWindowPos(
            window,
            insertAfter,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            flags);
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public IntPtr MonitorFromWindow(IntPtr window)
        => NativeMonitorFromWindow(window, MonitorDefaultToNearest);

    public bool TryGetMonitorWorkArea(
        IntPtr monitor,
        out NativeWindowRect workArea,
        out int error)
    {
        var info = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        var succeeded = NativeGetMonitorInfo(monitor, ref info);
        workArea = info.Work;
        error = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeWindowRect Monitor;
        public NativeWindowRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr NativeGetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(IntPtr window);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowRect",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetWindowRect(
        IntPtr window,
        out NativeWindowRect rect);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowPlacement",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetWindowPlacement(
        IntPtr window,
        ref NativeWindowPlacement placement);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowPlacement",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetWindowPlacement(
        IntPtr window,
        ref NativeWindowPlacement placement);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr NativeGetWindowLongPtr(
        IntPtr window,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowPos",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr NativeMonitorFromWindow(
        IntPtr window,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMonitorInfoW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetMonitorInfo(
        IntPtr monitor,
        ref NativeMonitorInfo info);
}
