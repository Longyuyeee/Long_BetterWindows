using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class TrayService : IDisposable
    {
        private readonly Window _mainWindow;
        private readonly HwndSource _hwndSource;
        private readonly uint _taskbarRestartMsg;
        private bool _visible;

        private const int WM_TRAYICON = 0x8001;
        private const int WM_TASKBARCREATED = 0x8002;

        public TrayService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.Closing += OnWindowClosing;

            var helper = new WindowInteropHelper(mainWindow);
            var hwnd = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(hwnd)!;
            _hwndSource.AddHook(WndProc);

            _taskbarRestartMsg = RegisterWindowMessage("TaskbarCreated");

            AddTrayIcon();

            Log.Information("系统托盘已启动");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int low = lParam.ToInt32() & 0xFFFF;

                if (low == 0x0203 || low == 0x0202) // WM_LBUTTONDBLCLK or WM_LBUTTONUP
                {
                    ShowWindow();
                }
                else if (low == 0x0205) // WM_RBUTTONUP
                {
                    ShowContextMenu();
                }

                handled = true;
            }
            else if (msg == _taskbarRestartMsg)
            {
                // Explorer 重启后重新创建托盘图标
                AddTrayIcon();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void AddTrayIcon()
        {
            if (_visible) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = 1,
                uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION),
                szTip = "Long窗口·全能助手",
            };

            _visible = Shell_NotifyIcon(NIM_ADD, ref nid);
        }

        private void RemoveTrayIcon()
        {
            if (!_visible) return;

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = 1,
            };

            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _visible = false;
        }

        private void ShowContextMenu()
        {
            // 使用 WPF 弹出菜单替代 Win32 右键菜单
            var menu = new System.Windows.Controls.ContextMenu();

            var showItem = new System.Windows.Controls.MenuItem { Header = "显示窗口" };
            showItem.Click += (_, _) => ShowWindow();

            var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(showItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (App.IsExiting) return;

            e.Cancel = true;
            _mainWindow.Hide();
            Log.Debug("窗口已最小化到托盘");
        }

        private void ShowWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void ExitApplication()
        {
            App.IsExiting = true;
            RemoveTrayIcon();
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            RemoveTrayIcon();
            _hwndSource.RemoveHook(WndProc);
        }

        #region P/Invoke

        private const uint NIM_ADD = 0;
        private const uint NIM_DELETE = 2;
        private const uint NIF_ICON = 2;
        private const uint NIF_MESSAGE = 1;
        private const uint NIF_TIP = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        private static readonly IntPtr IDI_APPLICATION = new(32512);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        #endregion
    }
}
