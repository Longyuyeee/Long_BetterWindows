using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Services;
using WindowManagerPlugin;

namespace LongBetterWindows.Tests;

public sealed class WindowInfoServiceTests
{
    private static readonly IntPtr TestWindow = new(42);

    [Fact]
    public void ApplyLayout_UsesMonitorWorkAreaAndPreservesTopmost()
    {
        var native = new FakeWindowNativeApi
        {
            Topmost = true,
            WorkArea = new NativeWindowRect(-1920, 0, 0, 1080),
        };
        var service = new WindowInfoService(native);

        var result = service.ApplyLayout(TestWindow, WindowLayout.Right);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Data?.Changed);
        Assert.Equal(-960, result.Data?.After?.X);
        Assert.Equal(960, result.Data?.After?.Width);
        Assert.Equal(1080, result.Data?.After?.Height);
        Assert.True(result.Data?.After?.IsTopmost);
        Assert.Equal(new NativeWindowRect(-960, 0, 0, 1080), native.Rect);
    }

    [Fact]
    public void ApplyForegroundLayout_RejectsMissingWindowWithoutMutation()
    {
        var native = new FakeWindowNativeApi
        {
            ForegroundWindow = IntPtr.Zero,
        };
        var service = new WindowInfoService(native);

        var result = service.ApplyForegroundLayout(WindowLayout.Left);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.NotFound, result.ErrorCode);
        Assert.Empty(native.PositionCalls);
        Assert.Empty(native.PlacementCalls);
    }

    [Fact]
    public void ApplyLayout_RejectsInvalidHandleWithoutMutation()
    {
        var native = new FakeWindowNativeApi { WindowExists = false };
        var service = new WindowInfoService(native);

        var result = service.ApplyLayout(TestWindow, WindowLayout.Left);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.NotFound, result.ErrorCode);
        Assert.Empty(native.PositionCalls);
        Assert.Empty(native.PlacementCalls);
    }

    [Fact]
    public void ApplyLayout_RestoresSnapshotAfterPartialMutationFailure()
    {
        var original = new NativeWindowRect(100, 120, 900, 720);
        var native = new FakeWindowNativeApi { Rect = original };
        native.PositionResults.Enqueue(new NativeMutationResult(
            Succeeded: false,
            Error: 5,
            Mutate: true));
        native.PositionResults.Enqueue(new NativeMutationResult(
            Succeeded: true,
            Error: 0,
            Mutate: true));
        var service = new WindowInfoService(native);

        var result = service.ApplyLayout(TestWindow, WindowLayout.Left);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.Win32Error, result.ErrorCode);
        Assert.Contains("SetWindowPos", result.ErrorMessage);
        Assert.Contains("5", result.ErrorMessage);
        Assert.True(result.Data?.RecoveryAttempted);
        Assert.True(result.Data?.RecoverySucceeded);
        Assert.Null(result.Data?.RecoveryErrorMessage);
        Assert.Equal(original, native.Rect);
        Assert.Equal(original.Left, result.Data?.After?.X);
        Assert.Equal(original.Width, result.Data?.After?.Width);
    }

    [Fact]
    public void ApplyLayout_PreservesOriginalFailureWhenRecoveryAlsoFails()
    {
        var native = new FakeWindowNativeApi();
        native.PositionResults.Enqueue(new NativeMutationResult(
            Succeeded: false,
            Error: 5,
            Mutate: true));
        native.PositionResults.Enqueue(new NativeMutationResult(
            Succeeded: false,
            Error: 32,
            Mutate: false));
        var service = new WindowInfoService(native);

        var result = service.ApplyLayout(TestWindow, WindowLayout.Left);

        Assert.False(result.IsSuccess);
        Assert.Contains("SetWindowPos", result.ErrorMessage);
        Assert.Contains("5", result.ErrorMessage);
        Assert.True(result.Data?.RecoveryAttempted);
        Assert.False(result.Data?.RecoverySucceeded);
        Assert.Equal(ApiErrorCode.Win32Error, result.Data?.RecoveryErrorCode);
        Assert.Contains("Restore SetWindowPos", result.Data?.RecoveryErrorMessage);
        Assert.Contains("32", result.Data?.RecoveryErrorMessage);
    }

    [Fact]
    public void ApplyLayout_NormalizesMaximizedWindowBeforeMoving()
    {
        var native = new FakeWindowNativeApi
        {
            Placement = FakeWindowNativeApi.CreatePlacement(
                showCommand: 3,
                normalPosition: new NativeWindowRect(100, 120, 900, 720)),
        };
        var service = new WindowInfoService(native);

        var result = service.ApplyLayout(TestWindow, WindowLayout.TopLeft);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(native.PlacementCalls);
        Assert.Equal(9u, native.PlacementCalls[0].ShowCommand);
        Assert.Equal(WindowDisplayState.Maximized, result.Data?.Before?.DisplayState);
        Assert.Equal(WindowDisplayState.Normal, result.Data?.After?.DisplayState);
    }

    [Fact]
    public void ToggleTopmost_ChangesAndVerifiesState()
    {
        var native = new FakeWindowNativeApi { Topmost = false };
        var service = new WindowInfoService(native);

        var result = service.ToggleTopmost(TestWindow);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Data?.Changed);
        Assert.False(result.Data?.Before?.IsTopmost);
        Assert.True(result.Data?.After?.IsTopmost);
        Assert.True(native.Topmost);
    }

    [Fact]
    public async Task PluginCommand_PropagatesTransactionalFailure()
    {
        var windowInfo = new StubWindowInfoService
        {
            LayoutResult = new HostApiResponse<WindowOperationOutcome>
            {
                IsSuccess = false,
                ErrorCode = ApiErrorCode.Win32Error,
                ErrorMessage = "SetWindowPos failed with Win32 error 5.",
                Data = new WindowOperationOutcome
                {
                    RecoveryAttempted = true,
                    RecoverySucceeded = true,
                },
            },
        };
        var plugin = new WindowManagerPluginImpl();
        await plugin.InitializeAsync(new StubHostApi(windowInfo));

        var result = await plugin.ExecuteCommandAsync(new PluginCommandInvocation
        {
            CommandId = "window.left",
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("SetWindowPos failed with Win32 error 5.", result.Message);
        Assert.Equal(WindowLayout.Left, windowInfo.LastLayout);
    }

    [Fact]
    public void ApplyLayout_WorksAgainstIsolatedNativeWindow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var window = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WsOverlapped,
            -32000,
            -32000,
            320,
            240,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, window);

        try
        {
            var service = new WindowInfoService();
            var result = service.ApplyLayout(window, WindowLayout.Left);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.Data?.Before);
            Assert.NotNull(result.Data?.After);
            Assert.True(result.Data?.After?.Width > 0);
            Assert.True(result.Data?.After?.Height > 0);
        }
        finally
        {
            _ = DestroyWindow(window);
        }
    }

    private sealed class FakeWindowNativeApi : IWindowNativeApi
    {
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly IntPtr HwndNoTopmost = new(-2);

        public IntPtr ForegroundWindow { get; set; } = TestWindow;
        public bool WindowExists { get; set; } = true;
        public NativeWindowRect Rect { get; set; } =
            new(100, 120, 900, 720);
        public NativeWindowPlacement Placement { get; set; } =
            CreatePlacement(1, new NativeWindowRect(100, 120, 900, 720));
        public bool Topmost { get; set; }
        public IntPtr Monitor { get; set; } = new(7);
        public NativeWindowRect WorkArea { get; set; } =
            new(0, 0, 1920, 1080);
        public Queue<NativeMutationResult> PositionResults { get; } = new();
        public Queue<NativeMutationResult> PlacementResults { get; } = new();
        public List<NativePositionCall> PositionCalls { get; } = new();
        public List<NativeWindowPlacement> PlacementCalls { get; } = new();

        public IntPtr GetForegroundWindow() => ForegroundWindow;

        public bool IsWindow(IntPtr window)
            => WindowExists && window == TestWindow;

        public bool TryGetWindowRect(
            IntPtr window,
            out NativeWindowRect rect,
            out int error)
        {
            rect = Rect;
            error = 0;
            return IsWindow(window);
        }

        public bool TryGetWindowPlacement(
            IntPtr window,
            out NativeWindowPlacement placement,
            out int error)
        {
            placement = Placement;
            error = 0;
            return IsWindow(window);
        }

        public bool TrySetWindowPlacement(
            IntPtr window,
            NativeWindowPlacement placement,
            out int error)
        {
            PlacementCalls.Add(placement);
            var result = Next(PlacementResults);
            error = result.Error;
            if (result.Succeeded || result.Mutate)
                Placement = placement;
            return result.Succeeded;
        }

        public bool TryGetTopmost(
            IntPtr window,
            out bool topmost,
            out int error)
        {
            topmost = Topmost;
            error = 0;
            return IsWindow(window);
        }

        public bool TrySetWindowPosition(
            IntPtr window,
            IntPtr insertAfter,
            NativeWindowRect rect,
            uint flags,
            out int error)
        {
            PositionCalls.Add(new NativePositionCall(insertAfter, rect, flags));
            var result = Next(PositionResults);
            error = result.Error;
            if (result.Succeeded || result.Mutate)
            {
                if ((flags & SwpNoMove) == 0 || (flags & SwpNoSize) == 0)
                    Rect = rect;
                if (insertAfter == HwndTopmost)
                    Topmost = true;
                else if (insertAfter == HwndNoTopmost)
                    Topmost = false;
            }
            return result.Succeeded;
        }

        public IntPtr MonitorFromWindow(IntPtr window) => Monitor;

        public bool TryGetMonitorWorkArea(
            IntPtr monitor,
            out NativeWindowRect workArea,
            out int error)
        {
            workArea = WorkArea;
            error = 0;
            return monitor == Monitor && monitor != IntPtr.Zero;
        }

        public static NativeWindowPlacement CreatePlacement(
            uint showCommand,
            NativeWindowRect normalPosition)
            => new()
            {
                Length = (uint)Marshal.SizeOf<NativeWindowPlacement>(),
                ShowCommand = showCommand,
                NormalPosition = normalPosition,
            };

        private static NativeMutationResult Next(
            Queue<NativeMutationResult> results)
            => results.TryDequeue(out var result)
                ? result
                : new NativeMutationResult(true, 0, true);
    }

    private readonly record struct NativeMutationResult(
        bool Succeeded,
        int Error,
        bool Mutate);

    private readonly record struct NativePositionCall(
        IntPtr InsertAfter,
        NativeWindowRect Rect,
        uint Flags);

    private sealed class StubWindowInfoService : IWindowInfoService
    {
        public HostApiResponse<WindowOperationOutcome> LayoutResult { get; init; } =
            HostApiResponse<WindowOperationOutcome>.Success(new());
        public WindowLayout? LastLayout { get; private set; }

        public Task<HostApiResponse<WindowInfo>> GetForegroundWindowInfoAsync()
            => throw new NotSupportedException();

        public Task<HostApiResponse<List<WindowInfo>>> GetVisibleWindowsAsync()
            => throw new NotSupportedException();

        public HostApiResponse<WindowOperationOutcome> ApplyForegroundLayout(
            WindowLayout layout)
        {
            LastLayout = layout;
            return LayoutResult;
        }

        public HostApiResponse<WindowOperationOutcome> ToggleForegroundTopmost()
            => LayoutResult;
    }

    private sealed class StubPluginSettingsService : IPluginSettingsService
    {
        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(
                HostApiResponse<string?>.Success(null));

        public Task<HostApiResponse> SetAsync(string key, string value)
            => Task.FromResult(HostApiResponse.Success());
    }

    private sealed class StubHostApi(IWindowInfoService windowInfo) : IHostApi
    {
        public string? LastAccessError => null;
        public bool HasCapability(string capability) => true;
        public IHotKeyService HotKey => null!;
        public IPluginSettingsService Settings { get; } =
            new StubPluginSettingsService();
        public IShellSelectionService ShellSelection => null!;
        public IADSService ADS => null!;
        public IRegistryService Registry => null!;
        public IStorageService Storage => null!;
        public IClipboardService Clipboard => null!;
        public INotificationService Notification => null!;
        public IFileOpsService FileOps => null!;
        public IWindowInfoService WindowInfo { get; } = windowInfo;
        public IScreenCaptureService ScreenCapture => null!;
        public IInputService Input => null!;
        public IProcessService Process => null!;
        public IHttpService Http => null!;
        public IShellExecuteService ShellExecute => null!;
        public IUICapability UI => null!;
        public INetworkPortService NetworkPort => null!;
        public IPerformanceService Performance => null!;
        public IFileSystemService FileSystem => null!;
        public IPinyinService Pinyin => null!;
        public ICacheService Cache => null!;
        public IScheduleService Schedule => null!;
        public IAudioService Audio => null!;
        public IPowerService Power => null!;
        public IThemeService Theme => null!;
        public IWallpaperService Wallpaper => null!;
        public IBrightnessService Brightness => null!;
        public INetworkMonitorService NetworkMonitor => null!;
    }

    private const uint WsOverlapped = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
