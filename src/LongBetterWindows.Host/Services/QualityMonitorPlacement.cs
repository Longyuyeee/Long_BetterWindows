using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LongBetterWindows.Host.Services;

internal sealed record QualityMonitorDescriptor(
    IntPtr Handle,
    string DeviceName,
    NativeWindowRect Bounds,
    NativeWindowRect WorkArea,
    bool IsPrimary,
    uint Dpi);

internal static class QualityMonitorPlacement
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    internal static QualityMonitorDescriptor? Select(
        IEnumerable<QualityMonitorDescriptor> monitors,
        string deviceName)
        => monitors.FirstOrDefault(monitor => string.Equals(
            monitor.DeviceName,
            deviceName.Trim(),
            StringComparison.OrdinalIgnoreCase));

    internal static bool TryApply(
        Window window,
        string deviceName,
        out QualityMonitorDescriptor? monitor,
        out string error)
    {
        monitor = Select(Enumerate(), deviceName);
        if (monitor is null)
        {
            error = $"Quality monitor was not found: {deviceName}";
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        var nativeApi = new WindowNativeApi();
        var nativeError = 0;
        if (handle == IntPtr.Zero
            || !nativeApi.TryGetWindowRect(handle, out _, out nativeError))
        {
            error = $"Quality window bounds are unavailable ({nativeError}).";
            return false;
        }

        var bounds = Views.PluginWindowPlacement.Calculate(
            monitor.WorkArea,
            Math.Max(1, window.Width),
            Math.Max(1, window.Height),
            monitor.Dpi,
            fullScreen: false);
        if (!nativeApi.TrySetWindowPosition(
                handle,
                IntPtr.Zero,
                bounds,
                SwpNoActivate | SwpNoZOrder,
                out nativeError))
        {
            error = $"Quality window could not move to {monitor.DeviceName} "
                + $"({nativeError}).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static bool TryDescribe(
        Window window,
        out QualityMonitorDescriptor? monitor,
        out string error)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitorHandle = handle == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMonitorFromWindow(handle, MonitorDefaultToNearest);
        monitor = Enumerate().FirstOrDefault(candidate =>
            candidate.Handle == monitorHandle);
        error = monitor is null
            ? "Quality window monitor could not be resolved."
            : string.Empty;
        return monitor is not null;
    }

    internal static IReadOnlyList<QualityMonitorDescriptor>
        EnumerateForQuality()
        => Enumerate();

    private static IReadOnlyList<QualityMonitorDescriptor> Enumerate()
    {
        var monitors = new List<QualityMonitorDescriptor>();
        NativeEnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (handle, _, _, _) =>
            {
                var info = new NativeMonitorInfo
                {
                    Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
                    DeviceName = string.Empty,
                };
                if (NativeGetMonitorInfo(handle, ref info))
                {
                    var dpi = 96u;
                    if (NativeGetDpiForMonitor(handle, 0, out var dpiX, out _) == 0
                        && dpiX > 0)
                    {
                        dpi = dpiX;
                    }
                    monitors.Add(new QualityMonitorDescriptor(
                        handle,
                        info.DeviceName,
                        info.Monitor,
                        info.Work,
                        (info.Flags & MonitorInfoPrimary) != 0,
                        dpi));
                }
                return true;
            },
            IntPtr.Zero);
        return monitors;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRect,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeWindowRect Monitor;
        public NativeWindowRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeEnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMonitorInfoW",
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeGetMonitorInfo(
        IntPtr monitor,
        ref NativeMonitorInfo info);

    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
    private static extern int NativeGetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr NativeMonitorFromWindow(
        IntPtr window,
        uint flags);
}
