using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public enum MouseGestureMode
    {
        Disabled,
        MiddleButton,
        LongRightPress,
    }

    public sealed class LongRightPressRecognizer
    {
        public const int HoldMilliseconds = 560;
        public const int MovementTolerance = 10;
        private long _startedAt;
        private int _startX;
        private int _startY;
        private bool _tracking;
        private bool _triggered;

        public void Begin(int x, int y, long timestamp)
        {
            _startX = x;
            _startY = y;
            _startedAt = timestamp;
            _tracking = true;
            _triggered = false;
        }

        public bool TryTrigger(int x, int y, long timestamp)
        {
            if (!_tracking || _triggered) return false;
            if (Math.Abs(x - _startX) > MovementTolerance
                || Math.Abs(y - _startY) > MovementTolerance)
            {
                _tracking = false;
                return false;
            }
            if (timestamp - _startedAt < HoldMilliseconds) return false;
            _triggered = true;
            return true;
        }

        public bool End()
        {
            var suppressContextMenu = _tracking && _triggered;
            Reset();
            return suppressContextMenu;
        }

        public void Reset()
        {
            _tracking = false;
            _triggered = false;
        }
    }

    public sealed class MouseGestureService : IDisposable
    {
        private const string StorageKey = "interaction.mouse-gesture.v1";
        private const int WhMouseLl = 14;
        private const int WmMouseMove = 0x0200;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private readonly IStorageService _storage;
        private readonly HostEnvironmentStateService _environmentState;
        private readonly LongRightPressRecognizer _longRight = new();
        private readonly DispatcherTimer _holdTimer;
        private readonly HookProc _hookCallback;
        private IntPtr _hook;
        private bool _suppressMiddleUp;
        private bool _started;

        public MouseGestureMode Mode { get; private set; }

        public MouseGestureService(IStorageService storage)
            : this(storage, HostEnvironmentStateService.Current)
        {
        }

        internal MouseGestureService(
            IStorageService storage,
            HostEnvironmentStateService environmentState)
        {
            _storage = storage;
            _environmentState = environmentState;
            _hookCallback = MouseHook;
            _holdTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LongRightPressRecognizer.HoldMilliseconds),
            };
            _holdTimer.Tick += HoldTimer_Tick;
            _environmentState.InteractionAvailabilityChanged +=
                EnvironmentState_InteractionAvailabilityChanged;
        }

        public async Task InitializeAsync()
        {
            var response = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (response.IsSuccess && !string.IsNullOrWhiteSpace(response.Data))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<MouseGesturePreference>(response.Data);
                    if (Enum.TryParse<MouseGestureMode>(stored?.Mode, true, out var mode))
                        Mode = mode;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "鼠标手势设置加载失败，保持默认关闭");
                }
            }
        }

        public void Start()
        {
            _started = true;
            if (ShouldInstallHook()) InstallHook();
        }

        public async Task SetModeAsync(MouseGestureMode mode)
        {
            Mode = mode;
            UninstallHook();
            if (ShouldInstallHook()) InstallHook();
            await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(
                new MouseGesturePreference { Mode = mode.ToString() }));
            Log.Information("超级面板鼠标手势已设置为 {Mode}", mode);
        }

        private void InstallHook()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWindowsHookEx(WhMouseLl, _hookCallback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                Log.Warning("超级面板鼠标手势安装失败: Win32Error={Error}",
                    Marshal.GetLastWin32Error());
        }

        private void UninstallHook()
        {
            _holdTimer.Stop();
            _longRight.Reset();
            _suppressMiddleUp = false;
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            if (Mode == MouseGestureMode.MiddleButton)
            {
                if (message == WmMButtonDown)
                {
                    _suppressMiddleUp = true;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        SuperPanelWindow.ShowPanel);
                    return new IntPtr(1);
                }
                if (message == WmMButtonUp && _suppressMiddleUp)
                {
                    _suppressMiddleUp = false;
                    return new IntPtr(1);
                }
            }
            else if (Mode == MouseGestureMode.LongRightPress)
            {
                if (message == WmRButtonDown)
                {
                    _longRight.Begin(data.Point.X, data.Point.Y, Environment.TickCount64);
                    _holdTimer.Stop();
                    _holdTimer.Start();
                }
                else if (message == WmMouseMove)
                {
                    _longRight.TryTrigger(data.Point.X, data.Point.Y, Environment.TickCount64);
                }
                else if (message == WmRButtonUp)
                {
                    _holdTimer.Stop();
                    if (_longRight.End()) return new IntPtr(1);
                }
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void HoldTimer_Tick(object? sender, EventArgs e)
        {
            _holdTimer.Stop();
            if (GetCursorPos(out var point)
                && _longRight.TryTrigger(point.X, point.Y, Environment.TickCount64))
                SuperPanelWindow.ShowPanel();
        }

        private void EnvironmentState_InteractionAvailabilityChanged(
            bool available)
        {
            if (!available)
            {
                UninstallHook();
                return;
            }

            if (ShouldInstallHook())
                InstallHook();
        }

        internal bool ShouldInstallHook()
            => ShouldInstallHook(
                _started,
                Mode,
                _environmentState.IsInteractionAvailable);

        internal static bool ShouldInstallHook(
            bool started,
            MouseGestureMode mode,
            bool interactionAvailable)
            => started
                && mode != MouseGestureMode.Disabled
                && interactionAvailable;

        public void Dispose()
        {
            _started = false;
            UninstallHook();
            _environmentState.InteractionAvailabilityChanged -=
                EnvironmentState_InteractionAvailabilityChanged;
            _holdTimer.Tick -= HoldTimer_Tick;
        }

        private sealed record MouseGesturePreference
        {
            public string Mode { get; init; } = MouseGestureMode.Disabled.ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MsllHookStruct
        {
            public Point Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId, HookProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
