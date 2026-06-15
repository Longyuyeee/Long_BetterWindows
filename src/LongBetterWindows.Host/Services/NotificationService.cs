using System.Runtime.InteropServices;
using System.Windows;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class NotificationService : INotificationService
    {
        private static IntPtr _trayHwnd;
        private static uint _trayId;

        /// <summary>设置托盘图标句柄，用于显示原生气泡通知</summary>
        public static void SetTrayHandle(IntPtr hwnd, uint id)
        {
            _trayHwnd = hwnd;
            _trayId = id;
        }

        public Task<HostApiResponse> ShowAsync(string title, string body)
        {
            try
            {
                // 优先尝试原生气泡通知
                if (_trayHwnd != IntPtr.Zero)
                {
                    var shown = ShowNativeBalloon(title, body);
                    if (shown)
                        return Task.FromResult(HostApiResponse.Success());
                }

                // 回退到自定义 FloatingHudWindow 浮窗
                Application.Current.Dispatcher.Invoke(() =>
                    FloatingHudWindow.ShowToast($"{title}\n{body}"));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "通知显示失败");
            }

            return Task.FromResult(HostApiResponse.Success());
        }

        private static bool ShowNativeBalloon(string title, string text)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _trayHwnd,
                uID = _trayId,
                uFlags = NIF_INFO,
                dwInfoFlags = NIIF_INFO,
                szInfoTitle = title ?? "",
                szInfo = text ?? "",
                uTimeoutOrVersion = 5000,
            };

            return Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        #region P/Invoke

        private const uint NIM_MODIFY = 1;
        private const uint NIF_INFO = 0x10;
        private const uint NIIF_INFO = 0x01;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
        }

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

        #endregion
    }
}
