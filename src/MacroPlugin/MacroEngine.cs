using System.Runtime.InteropServices;

namespace MacroPlugin;

public enum MacroState { Idle, Recording, Playing, PlayingLoop }

public class MacroAction
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsRightButton { get; init; }
    public int DelayMs { get; set; }
}

public class MacroEngine : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly HookProc _hookProc;
    private readonly List<MacroAction> _actions = new();
    private DateTime _lastEvent;
    private CancellationTokenSource? _playCts;
    private readonly object _lock = new();

    public MacroState State { get; private set; } = MacroState.Idle;
    public int ActionCount => _actions.Count;
    public event Action<MacroState>? StateChanged;

    public MacroEngine()
    {
        _hookProc = MouseHookCallback;
    }

    public void StartRecording()
    {
        lock (_lock)
        {
            _actions.Clear();
            _lastEvent = DateTime.UtcNow;
            _hookId = SetHook(_hookProc);
            State = MacroState.Recording;
        }
        StateChanged?.Invoke(State);
    }

    public void StopRecording()
    {
        lock (_lock)
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            State = MacroState.Idle;
        }
        StateChanged?.Invoke(State);
    }

    public async Task PlayOnceAsync()
    {
        await PlayInternal(loop: false);
    }

    public void PlayLoop()
    {
        _playCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            State = MacroState.PlayingLoop;
            StateChanged?.Invoke(State);

            while (!_playCts.Token.IsCancellationRequested)
            {
                await PlayInternal(loop: false);
                await Task.Delay(100, _playCts.Token);
            }
        }, _playCts.Token);
    }

    public void StopPlay()
    {
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
        State = MacroState.Idle;
        StateChanged?.Invoke(State);
    }

    private async Task PlayInternal(bool loop)
    {
        List<MacroAction> snapshot;
        lock (_lock) { snapshot = _actions.ToList(); }

        if (snapshot.Count == 0) return;

        if (!loop)
        {
            State = MacroState.Playing;
            StateChanged?.Invoke(State);
        }

        foreach (var action in snapshot)
        {
            SetCursorPos(action.X, action.Y);
            await Task.Delay(20);

            uint downFlag = action.IsRightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            uint upFlag = action.IsRightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

            SendMouseInput(action.X, action.Y, downFlag);
            await Task.Delay(30);
            SendMouseInput(action.X, action.Y, upFlag);

            if (action.DelayMs > 0)
                await Task.Delay(action.DelayMs);
        }

        if (!loop)
        {
            State = MacroState.Idle;
            StateChanged?.Invoke(State);
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();

            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var now = DateTime.UtcNow;

                lock (_lock)
                {
                    _actions.Add(new MacroAction
                    {
                        X = hookStruct.pt.X,
                        Y = hookStruct.pt.Y,
                        IsRightButton = msg == WM_RBUTTONDOWN,
                        DelayMs = (int)(now - _lastEvent).TotalMilliseconds,
                    });
                    _lastEvent = now;
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void SendMouseInput(int x, int y, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    mouseData = 0,
                    dwFlags = flags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };

        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        StopPlay();
        StopRecording();
    }

    #region P/Invoke

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr SetHook(HookProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule == null) return IntPtr.Zero;
        return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion
}
