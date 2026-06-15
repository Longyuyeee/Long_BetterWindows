using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class WindowInfoService : IWindowInfoService
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x0008;

        public Task<HostApiResponse<WindowInfo>> GetForegroundWindowInfoAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var hwnd = GetForegroundWindow();
                    return HostApiResponse<WindowInfo>.Success(GetInfo(hwnd));
                }
                catch (Exception ex) { return HostApiResponse<WindowInfo>.Failure(ApiErrorCode.Unknown, ex.Message); }
            });
        }

        public Task<HostApiResponse<List<WindowInfo>>> GetVisibleWindowsAsync()
        {
            return Task.Run(() =>
            {
                var list = new List<WindowInfo>();
                try
                {
                    EnumWindows((hwnd, _) =>
                    {
                        if (IsWindowVisible(hwnd))
                        {
                            var info = GetInfo(hwnd);
                            if (!string.IsNullOrEmpty(info.Title) && info.Width > 50)
                                list.Add(info);
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }
                return HostApiResponse<List<WindowInfo>>.Success(list);
            });
        }

        private static WindowInfo GetInfo(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            GetWindowRect(hwnd, out var r);
            GetWindowThreadProcessId(hwnd, out uint pid);
            var name = "";
            try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }

            return new WindowInfo
            {
                Title = sb.ToString(),
                ProcessName = name,
                X = r.L, Y = r.T,
                Width = r.R - r.L, Height = r.B - r.T,
                IsTopmost = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0,
            };
        }
    }
}
