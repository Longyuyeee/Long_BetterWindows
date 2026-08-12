using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LongBetterWindows.PluginSdk.Wpf;

public static class TransientWindowBehavior
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr window,
        int index,
        IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(
        IntPtr window,
        int index,
        int newValue);

    public static void MakeNonActivating(Window window, bool clickThrough)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyNativeStyles(window, clickThrough);
            return;
        }

        EventHandler? initialized = null;
        initialized = (_, _) =>
        {
            window.SourceInitialized -= initialized;
            ApplyNativeStyles(window, clickThrough);
        };
        window.SourceInitialized += initialized;
    }

    private static void ApplyNativeStyles(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "The transient window handle is not available.");

        Marshal.SetLastPInvokeError(0);
        var current = ReadWindowLong(handle, GwlExStyle);
        var error = Marshal.GetLastPInvokeError();
        if (current == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, "Unable to read transient window styles.");

        var styles = current.ToInt64() | WsExNoActivate | WsExToolWindow;
        if (clickThrough)
            styles |= WsExTransparent;

        Marshal.SetLastPInvokeError(0);
        var previous = WriteWindowLong(handle, GwlExStyle, styles);
        error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, "Unable to apply transient window styles.");
    }

    private static IntPtr ReadWindowLong(IntPtr window, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtr(window, index)
            : new IntPtr(GetWindowLong(window, index));

    private static IntPtr WriteWindowLong(IntPtr window, int index, long value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr(window, index, new IntPtr(value))
            : new IntPtr(SetWindowLong(window, index, unchecked((int)value)));
}
