using System.Runtime.InteropServices;
using System.Text.Json;

namespace MacroPlugin;

public enum MacroState { Idle, Recording, Playing, PlayingLoop }
public enum MacroActionType { MouseClick, KeyPress }

public class MacroAction
{
    public MacroActionType Type { get; init; } = MacroActionType.MouseClick;
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsRightButton { get; init; }
    public int KeyCode { get; init; }  // VK key code for keyboard
    public int DelayMs { get; set; }

    public static MacroAction Mouse(int x, int y, bool right, int delay)
        => new() { Type = MacroActionType.MouseClick, X = x, Y = y, IsRightButton = right, DelayMs = delay };

    public static MacroAction Key(int vk, int delay)
        => new() { Type = MacroActionType.KeyPress, KeyCode = vk, DelayMs = delay };
}

public class MacroEngine : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_KEYDOWN = 0x0100;

    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private readonly List<MacroAction> _actions = new();
    private DateTime _lastEvent;
    private CancellationTokenSource? _playCts;
    private Task? _playTask;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MacroState State { get; private set; } = MacroState.Idle;
    public int ActionCount
    {
        get
        {
            lock (_lock)
                return _actions.Count;
        }
    }
    public event Action<MacroState>? StateChanged;

    public MacroEngine()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;
    }

    public bool StartRecording()
    {
        if (State != MacroState.Idle)
            return false;

        lock (_lock) { _actions.Clear(); _lastEvent = DateTime.UtcNow; }
        _mouseHook = SetHook(WH_MOUSE_LL, _mouseProc);
        _keyboardHook = SetHook(WH_KEYBOARD_LL, _keyboardProc);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            StopRecording();
            return false;
        }
        State = MacroState.Recording;
        StateChanged?.Invoke(State);
        return true;
    }

    public void StopRecording()
    {
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        State = MacroState.Idle;
        StateChanged?.Invoke(State);
    }

    public async Task<bool> PlayOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (State != MacroState.Idle || ActionCount == 0)
            return false;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _playCts = cancellation;
        State = MacroState.Playing;
        StateChanged?.Invoke(State);
        try
        {
            await PlayActionsAsync(cancellation.Token);
            return true;
        }
        finally
        {
            if (ReferenceEquals(_playCts, cancellation))
            {
                _playCts = null;
                State = MacroState.Idle;
                StateChanged?.Invoke(State);
            }
            cancellation.Dispose();
        }
    }

    public bool PlayLoop()
    {
        if (State != MacroState.Idle || ActionCount == 0)
            return false;

        var cancellation = new CancellationTokenSource();
        _playCts = cancellation;
        State = MacroState.PlayingLoop;
        StateChanged?.Invoke(State);
        _playTask = Task.Run(() => PlayLoopAsync(cancellation));
        return true;
    }

    public void StopPlay() => _playCts?.Cancel();

    public string SaveToJson()
    {
        lock (_lock) return JsonSerializer.Serialize(_actions, JsonOpts);
    }

    public void LoadFromJson(string json)
    {
        lock (_lock)
        {
            _actions.Clear();
            var loaded = JsonSerializer.Deserialize<List<MacroAction>>(json);
            if (loaded != null) _actions.AddRange(loaded);
        }
    }

    private async Task PlayLoopAsync(CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await PlayActionsAsync(cancellation.Token);
                await Task.Delay(100, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_playCts, cancellation))
            {
                _playCts = null;
                _playTask = null;
                State = MacroState.Idle;
                StateChanged?.Invoke(State);
            }
            cancellation.Dispose();
        }
    }

    private async Task PlayActionsAsync(CancellationToken cancellationToken)
    {
        List<MacroAction> snapshot;
        lock (_lock) { snapshot = _actions.ToList(); }
        if (snapshot.Count == 0) return;

        foreach (var action in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.Type == MacroActionType.MouseClick)
            {
                SetCursorPos(action.X, action.Y);
                await Task.Delay(20, cancellationToken);
                uint down = action.IsRightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
                uint up = action.IsRightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
                SendMouseInput(action.X, action.Y, down);
                await Task.Delay(30, cancellationToken);
                SendMouseInput(action.X, action.Y, up);
            }
            else if (action.Type == MacroActionType.KeyPress)
            {
                keybd_event((byte)action.KeyCode, 0, 0, UIntPtr.Zero);
                await Task.Delay(30, cancellationToken);
                keybd_event((byte)action.KeyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            if (action.DelayMs > 0)
                await Task.Delay(action.DelayMs, cancellationToken);
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var now = DateTime.UtcNow;
                lock (_lock) _actions.Add(MacroAction.Mouse(hs.pt.X, hs.pt.Y, msg == WM_RBUTTONDOWN, (int)(now - _lastEvent).TotalMilliseconds));
                _lastEvent = now;
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var now = DateTime.UtcNow;
            lock (_lock) _actions.Add(MacroAction.Key((int)hs.vkCode, (int)(now - _lastEvent).TotalMilliseconds));
            _lastEvent = now;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void SendMouseInput(int x, int y, uint flags)
    {
        var virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var virtualWidth = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var virtualHeight = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        var normalizedX = NormalizeAbsoluteCoordinate(x, virtualLeft, virtualWidth);
        var normalizedY = NormalizeAbsoluteCoordinate(y, virtualTop, virtualHeight);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    dwFlags = flags | MOUSEEVENTF_ABSOLUTE
                        | MOUSEEVENTF_MOVE | MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    private static int NormalizeAbsoluteCoordinate(
        int coordinate,
        int origin,
        int extent)
        => (int)Math.Round(
            Math.Clamp(
                (coordinate - origin) / (double)Math.Max(1, extent - 1),
                0,
                1)
            * 65535);

    public void Dispose() { StopPlay(); StopRecording(); }

    #region P/Invoke
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);

    // 缓存进程模块句柄，避免每次 SetHook 都调用 GetCurrentProcess()
    private static readonly IntPtr _cachedModuleHandle = GetModuleHandle(
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName);

    static IntPtr SetHook(int type, HookProc proc)
    {
        return SetWindowsHookEx(type, proc, _cachedModuleHandle, 0);
    }

    const uint INPUT_MOUSE = 0, MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; }
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    #endregion
}
