using System.Text.Json;
using MacroPlugin;

namespace LongBetterWindows.Tests;

public sealed class MacroEngineTests
{
    [Fact]
    public async Task StopPlayAsync_ReleasesPressedMouseButtonAndWaits()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = CreateEngine(native);
        Load(engine, MacroAction.Mouse(320, 240, right: false, delay: 0));

        var playback = engine.PlayOnceAsync();
        await native.MouseDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopPlayAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(stopped);
        Assert.False(await playback);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Equal(
            new[]
            {
                new MouseInputCall(
                    320,
                    240,
                    MacroMouseButton.Left,
                    IsDown: true),
                new MouseInputCall(
                    320,
                    240,
                    MacroMouseButton.Left,
                    IsDown: false),
            },
            native.MouseInputs);
    }

    [Fact]
    public async Task StopPlayAsync_ReleasesPressedKeyAndWaits()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = CreateEngine(native);
        Load(engine, MacroAction.Key(0x41, delay: 0));

        var playback = engine.PlayOnceAsync();
        await native.KeyDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopPlayAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(stopped);
        Assert.False(await playback);
        Assert.Equal(
            new[]
            {
                new KeyInputCall(0x41, IsDown: true),
                new KeyInputCall(0x41, IsDown: false),
            },
            native.KeyInputs);
    }

    [Fact]
    public async Task StopPlayAsync_ReportsPressedKeyReleaseFailure()
    {
        var native = new FakeMacroNativeApi();
        native.KeyResults.Enqueue(new NativeResult(true, 0));
        native.KeyResults.Enqueue(new NativeResult(false, 5));
        native.KeyResults.Enqueue(new NativeResult(false, 5));
        native.KeyResults.Enqueue(new NativeResult(false, 5));
        var engine = CreateEngine(native);
        Load(engine, MacroAction.Key(0x45, delay: 0));

        _ = engine.PlayOnceAsync();
        await native.KeyDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopPlayAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(stopped);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Contains("Release key", engine.LastError);
        Assert.False(engine.PlayLoop());

        native.KeyResults.Enqueue(new NativeResult(true, 0));
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task PlaybackFailure_IsObservedAndReturnsEngineToIdle()
    {
        var native = new FakeMacroNativeApi();
        native.KeyResults.Enqueue(new NativeResult(false, 5));
        await using var engine = CreateEngine(native);
        Load(engine, MacroAction.Key(0x42, delay: 0));
        var failure = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += message => failure.TrySetResult(message);

        var played = await engine.PlayOnceAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));
        var message = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(played);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Contains("SendInput(key down)", message);
        Assert.Contains("5", engine.LastError);
    }

    [Fact]
    public async Task FailedKeyUp_IsRetriedByFinallyCleanup()
    {
        var native = new FakeMacroNativeApi();
        native.KeyResults.Enqueue(new NativeResult(true, 0));
        native.KeyResults.Enqueue(new NativeResult(false, 87));
        native.KeyResults.Enqueue(new NativeResult(true, 0));
        await using var engine = new MacroEngine(
            native,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10));
        Load(engine, MacroAction.Key(0x43, delay: 0));

        var played = await engine.PlayOnceAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(played);
        Assert.Equal(3, native.KeyInputs.Count);
        Assert.False(native.KeyInputs[1].IsDown);
        Assert.False(native.KeyInputs[2].IsDown);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Contains("key up", engine.LastError);
    }

    [Fact]
    public async Task LoopStop_CancelsAndWaitsForBackgroundTask()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = CreateEngine(native);
        Load(engine, MacroAction.Mouse(40, 50, right: true, delay: 0));

        Assert.True(engine.PlayLoop());
        await native.MouseDown.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await engine.StopPlayAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(stopped);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Contains(
            native.MouseInputs,
            call => call.Button == MacroMouseButton.Right
                && !call.IsDown);
    }

    [Fact]
    public async Task PartialHookInstallationFailure_UnhooksInstalledHook()
    {
        var native = new FakeMacroNativeApi();
        native.HookResults.Enqueue(new HookResult(new IntPtr(101), 0));
        native.HookResults.Enqueue(new HookResult(IntPtr.Zero, 5));
        await using var engine = CreateEngine(native);

        var started = engine.StartRecording();

        Assert.False(started);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Equal(new IntPtr(101), Assert.Single(native.UninstallCalls));
        Assert.Contains("keyboard", engine.LastError);
    }

    [Fact]
    public async Task HookCleanupFailure_EntersFaultedAndCanBeRetried()
    {
        var native = new FakeMacroNativeApi();
        native.UninstallResults.Enqueue(new NativeResult(false, 5));
        native.UninstallResults.Enqueue(new NativeResult(true, 0));
        native.UninstallResults.Enqueue(new NativeResult(true, 0));
        await using var engine = CreateEngine(native);
        Assert.True(engine.StartRecording());

        var firstStop = engine.StopRecording();

        Assert.False(firstStop);
        Assert.Equal(MacroState.Faulted, engine.State);
        Assert.Contains("UnhookWindowsHookEx(mouse)", engine.LastError);

        Assert.True(engine.StopRecording());
        Assert.Equal(MacroState.Idle, engine.State);
    }

    [Fact]
    public async Task ObserverException_DoesNotBreakPlaybackCleanup()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = new MacroEngine(
            native,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10));
        Load(engine, MacroAction.Key(0x44, delay: 0));
        engine.StateChanged += _ => throw new InvalidOperationException(
            "observer failure");

        var played = await engine.PlayOnceAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(played);
        Assert.Equal(MacroState.Idle, engine.State);
        Assert.Equal(2, native.KeyInputs.Count);
    }

    [Fact]
    public async Task Recording_PreservesModifierChordTransitions()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = CreateEngine(native);
        Assert.True(engine.StartRecording());

        native.RaiseKeyboard(0x0100, 0x11);
        native.RaiseKeyboard(0x0100, 0x43);
        native.RaiseKeyboard(0x0101, 0x43);
        native.RaiseKeyboard(0x0101, 0x11);
        Assert.True(engine.StopRecording());

        var actions = JsonSerializer.Deserialize<List<MacroAction>>(
            engine.SaveToJson());
        Assert.Collection(
            actions!,
            action => AssertTransition(action, MacroActionType.KeyDown, 0x11),
            action => AssertTransition(action, MacroActionType.KeyDown, 0x43),
            action => AssertTransition(action, MacroActionType.KeyUp, 0x43),
            action => AssertTransition(action, MacroActionType.KeyUp, 0x11));
    }

    [Fact]
    public async Task StopRecording_RemovesTrailingActivationChord()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = CreateEngine(native);
        Assert.True(engine.StartRecording());

        native.RaiseKeyboard(0x0100, 0x11);
        native.RaiseKeyboard(0x0104, 0x12);
        native.RaiseKeyboard(0x0100, 0x75);
        Assert.True(engine.StopRecording(discardTrailingPressedKeys: true));

        Assert.Equal(0, engine.ActionCount);
    }

    [Fact]
    public async Task Playback_WaitsBeforeActionAndReplaysChordTransitions()
    {
        var native = new FakeMacroNativeApi();
        await using var engine = new MacroEngine(
            native,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10));
        Load(
            engine,
            MacroAction.KeyTransition(0x11, isDown: true, delay: 150),
            MacroAction.KeyTransition(0x43, isDown: true, delay: 0),
            MacroAction.KeyTransition(0x43, isDown: false, delay: 0),
            MacroAction.KeyTransition(0x11, isDown: false, delay: 0));

        var playback = engine.PlayOnceAsync();
        await Task.Delay(30);
        Assert.Empty(native.KeyInputs);
        Assert.True(await playback.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            new[]
            {
                new KeyInputCall(0x11, IsDown: true),
                new KeyInputCall(0x43, IsDown: true),
                new KeyInputCall(0x43, IsDown: false),
                new KeyInputCall(0x11, IsDown: false),
            },
            native.KeyInputs);
    }

    private static MacroEngine CreateEngine(FakeMacroNativeApi native)
        => new(
            native,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

    private static void Load(
        MacroEngine engine,
        params MacroAction[] actions)
        => engine.LoadFromJson(JsonSerializer.Serialize(actions));

    private static void AssertTransition(
        MacroAction action,
        MacroActionType expectedType,
        int expectedKey)
    {
        Assert.Equal(expectedType, action.Type);
        Assert.Equal(expectedKey, action.KeyCode);
    }

    private sealed class FakeMacroNativeApi : IMacroNativeApi
    {
        private int _nextHook = 10;
        private MacroHookProc? _keyboardHook;
        private uint _keyboardVirtualKey;

        public Queue<HookResult> HookResults { get; } = new();
        public Queue<NativeResult> UninstallResults { get; } = new();
        public Queue<NativeResult> MouseResults { get; } = new();
        public Queue<NativeResult> KeyResults { get; } = new();
        public List<IntPtr> UninstallCalls { get; } = [];
        public List<MouseInputCall> MouseInputs { get; } = [];
        public List<KeyInputCall> KeyInputs { get; } = [];
        public TaskCompletionSource MouseDown { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource KeyDown { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IntPtr InstallHook(
            int hookType,
            MacroHookProc callback,
            out int error)
        {
            if (hookType == 13)
                _keyboardHook = callback;
            var result = HookResults.TryDequeue(out var queued)
                ? queued
                : new HookResult(new IntPtr(_nextHook++), 0);
            error = result.Error;
            return result.Handle;
        }

        public bool UninstallHook(IntPtr hook, out int error)
        {
            UninstallCalls.Add(hook);
            var result = Next(UninstallResults);
            error = result.Error;
            return result.Succeeded;
        }

        public IntPtr CallNextHook(
            int code,
            IntPtr message,
            IntPtr data)
            => IntPtr.Zero;

        public MacroMouseHookData ReadMouseHookData(IntPtr data)
            => new(10, 20);

        public MacroKeyboardHookData ReadKeyboardHookData(IntPtr data)
            => new(_keyboardVirtualKey);

        public void RaiseKeyboard(int message, uint virtualKey)
        {
            _keyboardVirtualKey = virtualKey;
            Assert.NotNull(_keyboardHook);
            _keyboardHook(0, new IntPtr(message), IntPtr.Zero);
        }

        public bool TrySetCursorPosition(
            int x,
            int y,
            out int error)
        {
            error = 0;
            return true;
        }

        public bool TrySendMouseButton(
            int x,
            int y,
            MacroMouseButton button,
            bool isDown,
            out int error)
        {
            MouseInputs.Add(new MouseInputCall(
                x,
                y,
                button,
                isDown));
            if (isDown)
                MouseDown.TrySetResult();
            var result = Next(MouseResults);
            error = result.Error;
            return result.Succeeded;
        }

        public bool TrySendKey(
            int virtualKey,
            bool isDown,
            out int error)
        {
            KeyInputs.Add(new KeyInputCall(virtualKey, isDown));
            if (isDown)
                KeyDown.TrySetResult();
            var result = Next(KeyResults);
            error = result.Error;
            return result.Succeeded;
        }

        private static NativeResult Next(Queue<NativeResult> results)
            => results.TryDequeue(out var result)
                ? result
                : new NativeResult(true, 0);
    }

    private readonly record struct HookResult(
        IntPtr Handle,
        int Error);

    private readonly record struct NativeResult(
        bool Succeeded,
        int Error);

    private readonly record struct MouseInputCall(
        int X,
        int Y,
        MacroMouseButton Button,
        bool IsDown);

    private readonly record struct KeyInputCall(
        int VirtualKey,
        bool IsDown);
}
