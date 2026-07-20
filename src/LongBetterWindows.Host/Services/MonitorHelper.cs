using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 多显示器辅助——获取鼠标/窗口所在屏幕的工作区域。
    /// 插件可使用此类替代 SystemParameters.WorkArea 来支持多屏。
    /// </summary>
    public static class MonitorHelper
    {
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>获取鼠标光标所在显示器的工作区域</summary>
        public static Rect GetCursorWorkArea()
        {
            GetCursorPos(out var pt);
            var hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(hMonitor, ref mi);
            return new Rect(mi.rcWork.Left, mi.rcWork.Top,
                mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
        }

        public static (Point Cursor, Rect WorkArea) GetCursorPlacement(Visual visual)
        {
            ArgumentNullException.ThrowIfNull(visual);
            GetCursorPos(out var cursor);
            var physicalWorkArea = GetCursorWorkArea();
            var source = PresentationSource.FromVisual(visual);
            var transform = source?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            var logicalCursor = transform.Transform(new Point(cursor.X, cursor.Y));
            var logicalTopLeft = transform.Transform(physicalWorkArea.TopLeft);
            var logicalBottomRight = transform.Transform(physicalWorkArea.BottomRight);
            return (
                logicalCursor,
                new Rect(logicalTopLeft, logicalBottomRight));
        }
    }
}
