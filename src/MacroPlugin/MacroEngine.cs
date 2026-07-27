using System.Text.Json;

namespace MacroPlugin;

public enum MacroState
{
    Idle,
    Recording,
    Playing,
    PlayingLoop,
    Faulted,
}

public enum MacroActionType
{
    MouseClick,
    KeyPress,
}

public class MacroAction
{
    public MacroActionType Type { get; init; } = MacroActionType.MouseClick;
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsRightButton { get; init; }
    public int KeyCode { get; init; }
    public int DelayMs { get; set; }

    public static MacroAction Mouse(
        int x,
        int y,
        bool right,
        int delay)
        => new()
        {
            Type = MacroActionType.MouseClick,
            X = x,
            Y = y,
            IsRightButton = right,
            DelayMs = delay,
        };

    public static MacroAction Key(int virtualKey, int delay)
        => new()
        {
            Type = MacroActionType.KeyPress,
            KeyCode = virtualKey,
            DelayMs = delay,
        };
}

public sealed class MacroEngine : IDisposable, IAsyncDisposable
{
    private const int MouseHookType = 14;
    private const int KeyboardHookType = 13;
    private const int LeftButtonDownMessage = 0x0201;
    private const int RightButtonDownMessage = 0x0204;
    private const int KeyDownMessage = 0x0100;
    private const int ReleaseAttemptCount = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IMacroNativeApi _native;
    private readonly TimeSpan _pressDuration;
    private readonly TimeSpan _loopInterval;
    private readonly MacroHookProc _mouseHookCallback;
    private readonly MacroHookProc _keyboardHookCallback;
    private readonly List<MacroAction> _actions = [];
    private readonly HashSet<int> _pendingKeyReleases = [];
    private readonly Dictionary<
        MacroMouseButton,
        (int X, int Y)> _pendingMouseReleases = [];
    private readonly object _sync = new();
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private DateTime _lastEvent;
    private CancellationTokenSource? _playCancellation;
    private Task<bool>? _playTask;
    private MacroState _state = MacroState.Idle;
    private string? _lastError;
    private bool _disposed;

    public MacroEngine()
        : this(
            new MacroNativeApi(),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(100))
    {
    }

    internal MacroEngine(
        IMacroNativeApi native,
        TimeSpan pressDuration,
        TimeSpan loopInterval)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _pressDuration = pressDuration;
        _loopInterval = loopInterval;
        _mouseHookCallback = MouseHookCallback;
        _keyboardHookCallback = KeyboardHookCallback;
    }

    public MacroState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public string? LastError
    {
        get
        {
            lock (_sync)
                return _lastError;
        }
    }

    public int ActionCount
    {
        get
        {
            lock (_sync)
                return _actions.Count;
        }
    }

    public event Action<MacroState>? StateChanged;
    public event Action<string>? PlaybackFailed;

    public bool StartRecording()
    {
        MacroState? stateToPublish = null;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_state != MacroState.Idle)
                return false;
            if (HasPendingInputReleases())
            {
                _lastError = "Previously pressed inputs have not been released.";
                return false;
            }

            _actions.Clear();
            _lastEvent = DateTime.UtcNow;
            _lastError = null;
            _mouseHook = _native.InstallHook(
                MouseHookType,
                _mouseHookCallback,
                out var mouseError);
            if (_mouseHook == IntPtr.Zero)
            {
                _lastError = NativeFailure(
                    "SetWindowsHookEx(mouse)",
                    mouseError);
                return false;
            }

            _keyboardHook = _native.InstallHook(
                KeyboardHookType,
                _keyboardHookCallback,
                out var keyboardError);
            if (_keyboardHook == IntPtr.Zero)
            {
                _lastError = NativeFailure(
                    "SetWindowsHookEx(keyboard)",
                    keyboardError);
                if (!_native.UninstallHook(
                        _mouseHook,
                        out var cleanupError))
                {
                    _lastError += " " + NativeFailure(
                        "UnhookWindowsHookEx(mouse)",
                        cleanupError);
                    _state = MacroState.Faulted;
                    stateToPublish = _state;
                }
                else
                {
                    _mouseHook = IntPtr.Zero;
                }
            }
            else
            {
                _state = MacroState.Recording;
                stateToPublish = _state;
            }
        }

        PublishState(stateToPublish);
        return stateToPublish == MacroState.Recording;
    }

    public bool StopRecording()
    {
        MacroState? stateToPublish = null;
        lock (_sync)
        {
            if (_mouseHook == IntPtr.Zero
                && _keyboardHook == IntPtr.Zero)
            {
                if (_state == MacroState.Recording
                    || _state == MacroState.Faulted)
                {
                    _state = MacroState.Idle;
                    stateToPublish = _state;
                }
            }
            else
            {
                var errors = new List<string>();
                TryReleaseHook(
                    ref _mouseHook,
                    "mouse",
                    errors);
                TryReleaseHook(
                    ref _keyboardHook,
                    "keyboard",
                    errors);
                if (errors.Count == 0)
                {
                    _lastError = null;
                    _state = MacroState.Idle;
                }
                else
                {
                    _lastError = string.Join(" ", errors);
                    _state = MacroState.Faulted;
                }
                stateToPublish = _state;
            }
        }

        PublishState(stateToPublish);
        return State == MacroState.Idle;
    }

    public Task<bool> PlayOnceAsync(
        CancellationToken cancellationToken = default)
    {
        MacroState? stateToPublish;
        Task<bool> task;
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_state != MacroState.Idle
                || _actions.Count == 0
                || HasPendingInputReleases())
            {
                return Task.FromResult(false);
            }

            _lastError = null;
            var cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            _playCancellation = cancellation;
            _state = MacroState.Playing;
            stateToPublish = _state;
            task = RunPlaybackAsync(
                cancellation,
                loop: false,
                startGate.Task);
            _playTask = task;
        }

        PublishState(stateToPublish);
        startGate.SetResult();
        return task;
    }

    public bool PlayLoop()
    {
        MacroState? stateToPublish;
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_state != MacroState.Idle
                || _actions.Count == 0
                || HasPendingInputReleases())
            {
                return false;
            }

            _lastError = null;
            var cancellation = new CancellationTokenSource();
            _playCancellation = cancellation;
            _state = MacroState.PlayingLoop;
            stateToPublish = _state;
            _playTask = RunPlaybackAsync(
                cancellation,
                loop: true,
                startGate.Task);
        }

        PublishState(stateToPublish);
        startGate.SetResult();
        return true;
    }

    public async Task<bool> StopPlayAsync()
    {
        Task<bool>? task;
        lock (_sync)
        {
            _playCancellation?.Cancel();
            task = _playTask;
        }

        if (task is null)
            return true;
        var succeeded = await task.ConfigureAwait(false);
        return succeeded || LastError is null;
    }

    public async Task<bool> StopAsync()
    {
        _ = await StopPlayAsync().ConfigureAwait(false);
        var releaseErrors = ReleasePressedInputs();
        var recordingStopped = StopRecording();
        return releaseErrors.Count == 0 && recordingStopped;
    }

    public string SaveToJson()
    {
        lock (_sync)
            return JsonSerializer.Serialize(_actions, JsonOptions);
    }

    public void LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_state != MacroState.Idle)
            {
                throw new InvalidOperationException(
                    "A macro cannot be loaded while the engine is active.");
            }

            var loaded =
                JsonSerializer.Deserialize<List<MacroAction>>(json)
                ?? [];
            _actions.Clear();
            _actions.AddRange(loaded);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!StopAsync().GetAwaiter().GetResult())
        {
            throw new InvalidOperationException(
                LastError ?? "Macro engine cleanup failed.");
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (!await StopAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                LastError ?? "Macro engine cleanup failed.");
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task<bool> RunPlaybackAsync(
        CancellationTokenSource cancellation,
        bool loop,
        Task startGate)
    {
        var succeeded = true;
        try
        {
            await startGate.ConfigureAwait(false);
            do
            {
                await PlayActionsAsync(
                    cancellation.Token).ConfigureAwait(false);
                if (loop)
                {
                    await Task.Delay(
                        _loopInterval,
                        cancellation.Token).ConfigureAwait(false);
                }
            }
            while (loop);
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            succeeded = loop;
        }
        catch (Exception exception)
        {
            succeeded = false;
            var message = exception.Message;
            lock (_sync)
                _lastError = message;
            PublishPlaybackFailure(message);
        }
        finally
        {
            MacroState? stateToPublish = null;
            lock (_sync)
            {
                if (ReferenceEquals(
                        _playCancellation,
                        cancellation))
                {
                    _playCancellation = null;
                    _playTask = null;
                    _state = MacroState.Idle;
                    stateToPublish = _state;
                }
            }
            cancellation.Dispose();
            PublishState(stateToPublish);
        }

        return succeeded;
    }

    private async Task PlayActionsAsync(
        CancellationToken cancellationToken)
    {
        List<MacroAction> snapshot;
        lock (_sync)
            snapshot = [.. _actions];

        Exception? playbackError = null;
        try
        {
            foreach (var action in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (action.Type)
                {
                    case MacroActionType.MouseClick:
                        await PlayMouseActionAsync(
                            action,
                            cancellationToken).ConfigureAwait(false);
                        break;
                    case MacroActionType.KeyPress:
                        await PlayKeyActionAsync(
                            action,
                            cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported macro action: {action.Type}.");
                }

                if (action.DelayMs > 0)
                {
                    await Task.Delay(
                        action.DelayMs,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            playbackError = exception;
            throw;
        }
        finally
        {
            var releaseErrors = ReleasePressedInputs();
            if (releaseErrors.Count > 0)
            {
                var releaseException = new InvalidOperationException(
                    string.Join(" ", releaseErrors));
                if (playbackError is null)
                    throw releaseException;
                throw new AggregateException(
                    "Macro playback failed and pressed inputs could not "
                    + "be fully released.",
                    playbackError,
                    releaseException);
            }
        }
    }

    private async Task PlayMouseActionAsync(
        MacroAction action,
        CancellationToken cancellationToken)
    {
        if (!_native.TrySetCursorPosition(
                action.X,
                action.Y,
                out var cursorError))
        {
            throw new InvalidOperationException(
                NativeFailure("SetCursorPos", cursorError));
        }

        var button = action.IsRightButton
            ? MacroMouseButton.Right
            : MacroMouseButton.Left;
        if (!_native.TrySendMouseButton(
                action.X,
                action.Y,
                button,
                isDown: true,
                out var downError))
        {
            throw new InvalidOperationException(
                NativeFailure("SendInput(mouse down)", downError));
        }
        lock (_sync)
            _pendingMouseReleases[button] = (action.X, action.Y);
        await Task.Delay(
            _pressDuration,
            cancellationToken).ConfigureAwait(false);
        if (!_native.TrySendMouseButton(
                action.X,
                action.Y,
                button,
                isDown: false,
                out var upError))
        {
            throw new InvalidOperationException(
                NativeFailure("SendInput(mouse up)", upError));
        }
        lock (_sync)
            _pendingMouseReleases.Remove(button);
    }

    private async Task PlayKeyActionAsync(
        MacroAction action,
        CancellationToken cancellationToken)
    {
        if (!_native.TrySendKey(
                action.KeyCode,
                isDown: true,
                out var downError))
        {
            throw new InvalidOperationException(
                NativeFailure("SendInput(key down)", downError));
        }
        lock (_sync)
            _pendingKeyReleases.Add(action.KeyCode);
        await Task.Delay(
            _pressDuration,
            cancellationToken).ConfigureAwait(false);
        if (!_native.TrySendKey(
                action.KeyCode,
                isDown: false,
                out var upError))
        {
            throw new InvalidOperationException(
                NativeFailure("SendInput(key up)", upError));
        }
        lock (_sync)
            _pendingKeyReleases.Remove(action.KeyCode);
    }

    private List<string> ReleasePressedInputs()
    {
        var errors = new List<string>();
        List<int> pressedKeys;
        List<KeyValuePair<MacroMouseButton, (int X, int Y)>>
            pressedButtons;
        lock (_sync)
        {
            pressedKeys = [.. _pendingKeyReleases];
            pressedButtons = [.. _pendingMouseReleases];
        }
        foreach (var key in pressedKeys)
        {
            var released = false;
            var error = 0;
            for (var attempt = 0;
                attempt < ReleaseAttemptCount && !released;
                attempt++)
            {
                released = _native.TrySendKey(
                    key,
                    isDown: false,
                    out error);
            }
            if (!released)
            {
                errors.Add(NativeFailure(
                    $"Release key {key}",
                    error));
            }
            else
            {
                lock (_sync)
                    _pendingKeyReleases.Remove(key);
            }
        }
        foreach (var pressedButton in pressedButtons)
        {
            var released = false;
            var error = 0;
            for (var attempt = 0;
                attempt < ReleaseAttemptCount && !released;
                attempt++)
            {
                released = _native.TrySendMouseButton(
                    pressedButton.Value.X,
                    pressedButton.Value.Y,
                    pressedButton.Key,
                    isDown: false,
                    out error);
            }
            if (!released)
            {
                errors.Add(NativeFailure(
                    $"Release {pressedButton.Key} mouse button",
                    error));
            }
            else
            {
                lock (_sync)
                    _pendingMouseReleases.Remove(pressedButton.Key);
            }
        }
        return errors;
    }

    private IntPtr MouseHookCallback(
        int code,
        IntPtr message,
        IntPtr data)
    {
        if (code >= 0
            && State == MacroState.Recording
            && (message.ToInt32() == LeftButtonDownMessage
                || message.ToInt32() == RightButtonDownMessage))
        {
            var hookData = _native.ReadMouseHookData(data);
            var now = DateTime.UtcNow;
            lock (_sync)
            {
                _actions.Add(MacroAction.Mouse(
                    hookData.X,
                    hookData.Y,
                    message.ToInt32() == RightButtonDownMessage,
                    ElapsedMilliseconds(now)));
                _lastEvent = now;
            }
        }
        return _native.CallNextHook(code, message, data);
    }

    private IntPtr KeyboardHookCallback(
        int code,
        IntPtr message,
        IntPtr data)
    {
        if (code >= 0
            && State == MacroState.Recording
            && message.ToInt32() == KeyDownMessage)
        {
            var hookData = _native.ReadKeyboardHookData(data);
            var now = DateTime.UtcNow;
            lock (_sync)
            {
                _actions.Add(MacroAction.Key(
                    checked((int)hookData.VirtualKey),
                    ElapsedMilliseconds(now)));
                _lastEvent = now;
            }
        }
        return _native.CallNextHook(code, message, data);
    }

    private int ElapsedMilliseconds(DateTime now)
        => (int)Math.Clamp(
            (now - _lastEvent).TotalMilliseconds,
            0,
            int.MaxValue);

    private void TryReleaseHook(
        ref IntPtr hook,
        string name,
        ICollection<string> errors)
    {
        if (hook == IntPtr.Zero)
            return;
        if (_native.UninstallHook(hook, out var error))
        {
            hook = IntPtr.Zero;
            return;
        }
        errors.Add(NativeFailure(
            $"UnhookWindowsHookEx({name})",
            error));
    }

    private void PublishState(MacroState? state)
    {
        if (state is null)
            return;
        PublishHandlers(
            StateChanged,
            state.Value);
    }

    private void PublishPlaybackFailure(string message)
        => PublishHandlers(PlaybackFailed, message);

    private static void PublishHandlers<T>(
        Action<T>? handlers,
        T value)
    {
        if (handlers is null)
            return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(value);
            }
            catch
            {
            }
        }
    }

    private static string NativeFailure(
        string operation,
        int error)
        => error == 0
            ? $"{operation} failed."
            : $"{operation} failed with Win32 error {error}.";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private bool HasPendingInputReleases()
        => _pendingKeyReleases.Count > 0
            || _pendingMouseReleases.Count > 0;
}
