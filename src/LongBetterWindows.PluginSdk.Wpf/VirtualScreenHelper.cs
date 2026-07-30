using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LongBetterWindows.PluginSdk.Wpf;

public static class VirtualScreenHelper
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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

    public static Int32Rect GetPhysicalBounds()
    {
        var bounds = new Int32Rect(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException(
                "Virtual screen bounds are unavailable.");
        return bounds;
    }

    public static void PlaceWindowOverPhysicalBounds(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "The overlay window handle is not available.");

        var bounds = GetPhysicalBounds();
        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SwpNoActivate | SwpNoZOrder))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to place the overlay over the virtual screen.");
        }
    }
}
