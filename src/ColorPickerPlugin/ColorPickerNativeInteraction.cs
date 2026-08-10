using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ColorPickerPlugin;

public static class ColorPickerPopupPlacement
{
    public static Int32Rect Calculate(
        Int32Rect workArea,
        int cursorX,
        int cursorY,
        int popupWidth,
        int popupHeight,
        int gap = 18,
        int margin = 8)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        if (popupWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(popupWidth));
        if (popupHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(popupHeight));

        gap = Math.Max(0, gap);
        margin = Math.Max(0, margin);
        var width = Math.Min(popupWidth, workArea.Width);
        var height = Math.Min(popupHeight, workArea.Height);
        var left = SelectAxis(
            cursorX,
            width,
            workArea.X,
            workArea.X + workArea.Width,
            gap,
            margin);
        var top = SelectAxis(
            cursorY,
            height,
            workArea.Y,
            workArea.Y + workArea.Height,
            gap,
            margin);
        return new Int32Rect(left, top, width, height);
    }

    private static int SelectAxis(
        int cursor,
        int extent,
        int minimum,
        int maximum,
        int gap,
        int margin)
    {
        var constrainedMinimum = Math.Min(minimum + margin, maximum - extent);
        var constrainedMaximum = Math.Max(constrainedMinimum, maximum - margin - extent);
        var after = cursor + gap;
        if (after <= constrainedMaximum)
            return Math.Clamp(after, constrainedMinimum, constrainedMaximum);

        var before = cursor - gap - extent;
        return Math.Clamp(before, constrainedMinimum, constrainedMaximum);
    }
}

internal readonly record struct ColorPickerPhysicalPoint(int X, int Y);

internal static class ColorPickerNativeWindow
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    public static bool TryGetCursorPosition(out ColorPickerPhysicalPoint point)
    {
        if (GetCursorPos(out var nativePoint))
        {
            point = new ColorPickerPhysicalPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    public static void PositionNearCursor(
        Window window,
        ColorPickerPhysicalPoint point)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Color picker window handle is unavailable.");
        if (!GetWindowRect(handle, out var windowBounds))
            throw CreateWin32Exception("GetWindowRect");

        var monitor = MonitorFromPoint(
            new NativePoint { X = point.X, Y = point.Y },
            MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            throw CreateWin32Exception("GetMonitorInfo");

        var workArea = new Int32Rect(
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Width,
            monitorInfo.WorkArea.Height);
        var placement = ColorPickerPopupPlacement.Calculate(
            workArea,
            point.X,
            point.Y,
            windowBounds.Width,
            windowBounds.Height);
        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                SwpNoActivate | SwpNoZOrder))
        {
            throw CreateWin32Exception("SetWindowPos");
        }
    }

    private static Win32Exception CreateWin32Exception(string operation)
        => new(Marshal.GetLastWin32Error(), $"{operation} failed.");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}

internal sealed class ColorPickerPointerCapture : IDisposable
{
    private const int LowLevelMouseHook = 14;
    private const int LeftButtonDown = 0x0201;
    private const int LeftButtonUp = 0x0202;

    private readonly HookProcedure _hookProcedure;
    private IntPtr _hook;
    private bool _suppressLeftButton;
    private TaskCompletionSource? _leftButtonReleased;

    public ColorPickerPointerCapture()
    {
        _hookProcedure = HookCallback;
    }

    public event Action<ColorPickerPhysicalPoint>? LeftButtonPressed;

    public bool TryStart(out Win32Exception? error)
    {
        if (_hook != IntPtr.Zero)
        {
            error = null;
            return true;
        }

        _hook = SetWindowsHookEx(
            LowLevelMouseHook,
            _hookProcedure,
            GetModuleHandle(null),
            0);
        if (_hook != IntPtr.Zero)
        {
            error = null;
            return true;
        }

        error = new Win32Exception(
            Marshal.GetLastWin32Error(),
            "Unable to capture the next screen click.");
        return false;
    }

    public Task WaitForLeftButtonReleaseAsync(CancellationToken cancellationToken)
        => _suppressLeftButton && _leftButtonReleased is not null
            ? _leftButtonReleased.Task.WaitAsync(cancellationToken)
            : Task.CompletedTask;

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message == (IntPtr)LeftButtonDown)
        {
            if (!_suppressLeftButton)
            {
                _suppressLeftButton = true;
                _leftButtonReleased = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var mouse = Marshal.PtrToStructure<LowLevelMouseInput>(data);
                try
                {
                    LeftButtonPressed?.Invoke(new ColorPickerPhysicalPoint(
                        mouse.Point.X,
                        mouse.Point.Y));
                }
                catch
                {
                    _suppressLeftButton = false;
                    _leftButtonReleased.TrySetResult();
                    return CallNextHookEx(_hook, code, message, data);
                }
            }
            return (IntPtr)1;
        }

        if (code >= 0
            && message == (IntPtr)LeftButtonUp
            && _suppressLeftButton)
        {
            _suppressLeftButton = false;
            _leftButtonReleased?.TrySetResult();
            return (IntPtr)1;
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero)
            UnhookWindowsHookEx(hook);
        _suppressLeftButton = false;
        _leftButtonReleased?.TrySetResult();
    }

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public ColorPickerNativeWindow.NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
