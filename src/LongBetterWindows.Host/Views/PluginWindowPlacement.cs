using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views;

internal static class PluginWindowPlacement
{
    private const uint DefaultDpi = 96;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    internal static NativeWindowRect Calculate(
        NativeWindowRect workArea,
        double widthDip,
        double heightDip,
        uint dpi,
        bool fullScreen)
    {
        if (fullScreen)
            return workArea;

        var scale = (dpi == 0 ? DefaultDpi : dpi) / (double)DefaultDpi;
        var width = Math.Clamp(
            (int)Math.Ceiling(widthDip * scale),
            1,
            workArea.Width);
        var height = Math.Clamp(
            (int)Math.Ceiling(heightDip * scale),
            1,
            workArea.Height);
        var left = workArea.Left + ((workArea.Width - width) / 2);
        var top = workArea.Top + ((workArea.Height - height) / 2);
        return new NativeWindowRect(left, top, left + width, top + height);
    }

    internal static NativeWindowRect Constrain(
        NativeWindowRect bounds,
        NativeWindowRect workArea)
    {
        if (!IsValid(workArea))
            return bounds;

        var width = Math.Clamp(bounds.Width, 1, workArea.Width);
        var height = Math.Clamp(bounds.Height, 1, workArea.Height);
        var left = Math.Clamp(
            bounds.Left,
            workArea.Left,
            workArea.Right - width);
        var top = Math.Clamp(
            bounds.Top,
            workArea.Top,
            workArea.Bottom - height);
        return new NativeWindowRect(left, top, left + width, top + height);
    }

    internal static bool TryApply(
        Window window,
        Window? source,
        bool fullScreen)
    {
        if (source is null)
            return false;

        var windowHandle = new WindowInteropHelper(window).Handle;
        var sourceHandle = new WindowInteropHelper(source).Handle;
        if (windowHandle == IntPtr.Zero || sourceHandle == IntPtr.Zero)
            return false;

        var nativeApi = new WindowNativeApi();
        var monitor = nativeApi.MonitorFromWindow(sourceHandle);
        if (monitor == IntPtr.Zero
            || !nativeApi.TryGetMonitorWorkArea(
                monitor,
                out var workArea,
                out _)
            || !IsValid(workArea))
        {
            return false;
        }

        var windowMonitor = nativeApi.MonitorFromWindow(windowHandle);
        var dpi = NativeGetDpiForWindow(
            windowMonitor == monitor ? windowHandle : sourceHandle);
        var bounds = Calculate(
            workArea,
            window.Width,
            window.Height,
            dpi,
            fullScreen);
        return nativeApi.TrySetWindowPosition(
            windowHandle,
            IntPtr.Zero,
            bounds,
            SwpNoActivate | SwpNoZOrder,
            out _);
    }

    internal static bool TryConstrainToNearestWorkArea(Window window)
    {
        if (window.WindowState != WindowState.Normal)
            return true;

        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
            return false;

        var nativeApi = new WindowNativeApi();
        if (!nativeApi.TryGetWindowRect(windowHandle, out var bounds, out _))
            return false;

        var monitor = nativeApi.MonitorFromWindow(windowHandle);
        if (monitor == IntPtr.Zero
            || !nativeApi.TryGetMonitorWorkArea(
                monitor,
                out var workArea,
                out _)
            || !IsValid(workArea))
        {
            return false;
        }

        var constrained = Constrain(bounds, workArea);
        if (constrained == bounds)
            return true;

        return nativeApi.TrySetWindowPosition(
            windowHandle,
            IntPtr.Zero,
            constrained,
            SwpNoActivate | SwpNoZOrder,
            out _);
    }

    private static bool IsValid(NativeWindowRect workArea)
        => workArea.Width > 0 && workArea.Height > 0;

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint NativeGetDpiForWindow(IntPtr window);
}
