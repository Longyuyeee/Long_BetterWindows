using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services;

public sealed class WindowInfoService : IWindowInfoService
{
    private const uint SwRestore = 9;
    private const uint SwShowMinimized = 2;
    private const uint SwShowMaximized = 3;
    private const uint SwMinimize = 6;
    private const uint SwShowMinNoActive = 7;
    private const uint SwForceMinimize = 11;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);

    private readonly IWindowNativeApi _native;

    public WindowInfoService()
        : this(new WindowNativeApi())
    {
    }

    internal WindowInfoService(IWindowNativeApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public Task<HostApiResponse<WindowInfo>> GetForegroundWindowInfoAsync()
    {
        return Task.Run(() =>
        {
            var window = _native.GetForegroundWindow();
            if (window == IntPtr.Zero || !_native.IsWindow(window))
            {
                return HostApiResponse<WindowInfo>.Failure(
                    ApiErrorCode.NotFound,
                    "No foreground window is available.");
            }

            return TryGetInfo(window);
        });
    }

    public Task<HostApiResponse<List<WindowInfo>>> GetVisibleWindowsAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<WindowInfo>();
            var enumerationError = 0;
            var succeeded = EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window))
                    return true;

                var info = TryGetInfo(window);
                if (info.IsSuccess
                    && info.Data is { } value
                    && !string.IsNullOrEmpty(value.Title)
                    && value.Width > 50)
                {
                    list.Add(value);
                }
                return true;
            }, IntPtr.Zero);
            if (!succeeded)
                enumerationError = Marshal.GetLastWin32Error();

            return succeeded
                ? HostApiResponse<List<WindowInfo>>.Success(list)
                : HostApiResponse<List<WindowInfo>>.Failure(
                    ApiErrorCode.Win32Error,
                    Win32Failure("EnumWindows", enumerationError));
        });
    }

    public HostApiResponse<WindowOperationOutcome> ApplyForegroundLayout(
        WindowLayout layout)
    {
        var window = _native.GetForegroundWindow();
        if (window == IntPtr.Zero || !_native.IsWindow(window))
        {
            return Failure(
                ApiErrorCode.NotFound,
                "No foreground window is available.");
        }

        return ApplyLayout(window, layout);
    }

    public HostApiResponse<WindowOperationOutcome> ToggleForegroundTopmost()
    {
        var window = _native.GetForegroundWindow();
        if (window == IntPtr.Zero || !_native.IsWindow(window))
        {
            return Failure(
                ApiErrorCode.NotFound,
                "No foreground window is available.");
        }

        return ToggleTopmost(window);
    }

    internal HostApiResponse<WindowOperationOutcome> ApplyLayout(
        IntPtr window,
        WindowLayout layout)
    {
        if (window == IntPtr.Zero || !_native.IsWindow(window))
        {
            return Failure(
                ApiErrorCode.NotFound,
                "The target window is no longer available.");
        }

        var snapshot = CaptureSnapshot(window);
        if (!snapshot.IsSuccess || snapshot.Data is null)
            return SnapshotFailure(snapshot);

        if (layout == WindowLayout.Maximize)
            return Maximize(window, snapshot.Data);

        var monitor = _native.MonitorFromWindow(window);
        if (monitor == IntPtr.Zero)
        {
            return Failure(
                ApiErrorCode.NotFound,
                "No monitor is available for the target window.",
                snapshot.Data.Info);
        }
        if (!_native.TryGetMonitorWorkArea(
                monitor,
                out var workArea,
                out var monitorError))
        {
            return Failure(
                ApiErrorCode.Win32Error,
                Win32Failure("GetMonitorInfo", monitorError),
                snapshot.Data.Info);
        }
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return Failure(
                ApiErrorCode.InvalidArgument,
                "The monitor work area is invalid.",
                snapshot.Data.Info);
        }

        var normalized = snapshot.Data.Placement;
        if (GetDisplayState(normalized.ShowCommand) != WindowDisplayState.Normal)
        {
            normalized.ShowCommand = SwRestore;
            if (!_native.TrySetWindowPlacement(
                    window,
                    normalized,
                    out var normalizeError))
            {
                return FailureWithRecovery(
                    window,
                    snapshot.Data,
                    "SetWindowPlacement",
                    normalizeError);
            }
        }

        var target = CalculateTarget(workArea, layout);
        if (!_native.TrySetWindowPosition(
                window,
                IntPtr.Zero,
                target,
                SwpNoZOrder | SwpNoActivate | SwpShowWindow,
                out var positionError))
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "SetWindowPos",
                positionError);
        }

        var after = CaptureSnapshot(window);
        if (!after.IsSuccess || after.Data is null)
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "VerifyWindowState",
                0,
                after.ErrorMessage);
        }
        if (!RectEquals(after.Data.Rect, target)
            || after.Data.Topmost != snapshot.Data.Topmost)
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "VerifyWindowState",
                0,
                "The target rectangle or topmost state did not match.");
        }

        return HostApiResponse<WindowOperationOutcome>.Success(new()
        {
            Changed = !RectEquals(snapshot.Data.Rect, after.Data.Rect)
                || snapshot.Data.Info.DisplayState
                    != after.Data.Info.DisplayState,
            Before = snapshot.Data.Info,
            After = after.Data.Info,
        });
    }

    private HostApiResponse<WindowOperationOutcome> Maximize(
        IntPtr window,
        WindowSnapshot snapshot)
    {
        if (snapshot.Info.DisplayState == WindowDisplayState.Maximized)
        {
            return HostApiResponse<WindowOperationOutcome>.Success(new()
            {
                Changed = false,
                Before = snapshot.Info,
                After = snapshot.Info,
            });
        }

        var maximized = snapshot.Placement;
        maximized.ShowCommand = SwShowMaximized;
        if (!_native.TrySetWindowPlacement(
                window,
                maximized,
                out var placementError))
        {
            return FailureWithRecovery(
                window,
                snapshot,
                "SetWindowPlacement(maximize)",
                placementError);
        }

        var after = CaptureSnapshot(window);
        if (!after.IsSuccess || after.Data is null)
        {
            return FailureWithRecovery(
                window,
                snapshot,
                "VerifyWindowState",
                0,
                after.ErrorMessage);
        }
        if (after.Data.Info.DisplayState != WindowDisplayState.Maximized
            || after.Data.Topmost != snapshot.Topmost)
        {
            return FailureWithRecovery(
                window,
                snapshot,
                "VerifyMaximizedState",
                0,
                "The window did not enter the maximized state.");
        }

        return HostApiResponse<WindowOperationOutcome>.Success(new()
        {
            Changed = true,
            Before = snapshot.Info,
            After = after.Data.Info,
        });
    }

    internal HostApiResponse<WindowOperationOutcome> ToggleTopmost(
        IntPtr window)
    {
        if (window == IntPtr.Zero || !_native.IsWindow(window))
        {
            return Failure(
                ApiErrorCode.NotFound,
                "The target window is no longer available.");
        }

        var snapshot = CaptureSnapshot(window);
        if (!snapshot.IsSuccess || snapshot.Data is null)
            return SnapshotFailure(snapshot);

        var desired = !snapshot.Data.Topmost;
        if (!_native.TrySetWindowPosition(
                window,
                desired ? HwndTopmost : HwndNoTopmost,
                snapshot.Data.Rect,
                SwpNoMove | SwpNoSize | SwpNoActivate,
                out var positionError))
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "SetWindowPos",
                positionError);
        }
        if (!_native.TryGetTopmost(
                window,
                out var actualTopmost,
                out var styleError))
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "GetWindowLongPtr",
                styleError);
        }
        if (actualTopmost != desired)
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "VerifyTopmost",
                0,
                "The topmost state did not change.");
        }

        var after = CaptureSnapshot(window);
        if (!after.IsSuccess || after.Data is null)
        {
            return FailureWithRecovery(
                window,
                snapshot.Data,
                "VerifyWindowState",
                0,
                after.ErrorMessage);
        }

        return HostApiResponse<WindowOperationOutcome>.Success(new()
        {
            Changed = true,
            Before = snapshot.Data.Info,
            After = after.Data.Info,
        });
    }

    private HostApiResponse<WindowSnapshot> CaptureSnapshot(IntPtr window)
    {
        if (!_native.TryGetWindowRect(
                window,
                out var rect,
                out var rectError))
        {
            return HostApiResponse<WindowSnapshot>.Failure(
                ApiErrorCode.Win32Error,
                Win32Failure("GetWindowRect", rectError));
        }
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return HostApiResponse<WindowSnapshot>.Failure(
                ApiErrorCode.InvalidArgument,
                "The target window rectangle is invalid.");
        }
        if (!_native.TryGetWindowPlacement(
                window,
                out var placement,
                out var placementError))
        {
            return HostApiResponse<WindowSnapshot>.Failure(
                ApiErrorCode.Win32Error,
                Win32Failure("GetWindowPlacement", placementError));
        }
        if (!_native.TryGetTopmost(
                window,
                out var topmost,
                out var styleError))
        {
            return HostApiResponse<WindowSnapshot>.Failure(
                ApiErrorCode.Win32Error,
                Win32Failure("GetWindowLongPtr", styleError));
        }

        return HostApiResponse<WindowSnapshot>.Success(new(
            rect,
            placement,
            topmost,
            CreateInfo(window, rect, placement, topmost)));
    }

    private HostApiResponse<WindowInfo> TryGetInfo(IntPtr window)
    {
        var snapshot = CaptureSnapshot(window);
        return snapshot.IsSuccess && snapshot.Data is not null
            ? HostApiResponse<WindowInfo>.Success(snapshot.Data.Info)
            : HostApiResponse<WindowInfo>.Failure(
                snapshot.ErrorCode,
                snapshot.ErrorMessage);
    }

    private WindowInfo CreateInfo(
        IntPtr window,
        NativeWindowRect rect,
        NativeWindowPlacement placement,
        bool topmost)
    {
        var title = new StringBuilder(512);
        _ = GetWindowText(window, title, title.Capacity);
        _ = GetWindowThreadProcessId(window, out var processId);
        var processName = string.Empty;
        try
        {
            if (processId != 0)
                processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
        }

        return new WindowInfo
        {
            Title = title.ToString(),
            ProcessName = processName,
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
            IsTopmost = topmost,
            DisplayState = GetDisplayState(placement.ShowCommand),
        };
    }

    private HostApiResponse<WindowOperationOutcome> FailureWithRecovery(
        IntPtr window,
        WindowSnapshot snapshot,
        string operation,
        int error,
        string? detail = null)
    {
        var originalMessage = detail ?? Win32Failure(operation, error);
        var recovery = Restore(window, snapshot);
        return new HostApiResponse<WindowOperationOutcome>
        {
            IsSuccess = false,
            ErrorCode = ApiErrorCode.Win32Error,
            ErrorMessage = originalMessage,
            Data = new WindowOperationOutcome
            {
                RecoveryAttempted = true,
                RecoverySucceeded = recovery.IsSuccess,
                RecoveryErrorCode = recovery.ErrorCode,
                RecoveryErrorMessage = recovery.ErrorMessage,
                Before = snapshot.Info,
                After = recovery.Data,
            },
        };
    }

    private HostApiResponse<WindowInfo> Restore(
        IntPtr window,
        WindowSnapshot snapshot)
    {
        var failures = new List<string>();
        var recoveryError = 0;
        if (!_native.TrySetWindowPosition(
                window,
                snapshot.Topmost ? HwndTopmost : HwndNoTopmost,
                snapshot.Rect,
                SwpNoActivate,
                out var positionError))
        {
            recoveryError = positionError;
            failures.Add(Win32Failure(
                "Restore SetWindowPos",
                positionError));
        }
        if (!_native.TrySetWindowPlacement(
                window,
                snapshot.Placement,
                out var placementError))
        {
            if (recoveryError == 0)
                recoveryError = placementError;
            failures.Add(Win32Failure(
                "Restore SetWindowPlacement",
                placementError));
        }

        var restored = CaptureSnapshot(window);
        if (!restored.IsSuccess || restored.Data is null)
        {
            failures.Add(
                restored.ErrorMessage ?? "Restored state could not be verified.");
        }
        else if (!SnapshotEquals(snapshot, restored.Data))
        {
            failures.Add("Restored state does not match the original snapshot.");
        }

        return failures.Count == 0 && restored.Data is not null
            ? HostApiResponse<WindowInfo>.Success(restored.Data.Info)
            : HostApiResponse<WindowInfo>.Failure(
                ApiErrorCode.Win32Error,
                $"{string.Join(" ", failures)}"
                + (recoveryError == 0
                    ? string.Empty
                    : $" Recovery error: {recoveryError}."));
    }

    private static HostApiResponse<WindowOperationOutcome> SnapshotFailure(
        HostApiResponse<WindowSnapshot> snapshot)
        => Failure(snapshot.ErrorCode, snapshot.ErrorMessage);

    private static HostApiResponse<WindowOperationOutcome> Failure(
        ApiErrorCode code,
        string? message,
        WindowInfo? before = null)
        => new()
        {
            IsSuccess = false,
            ErrorCode = code,
            ErrorMessage = message,
            Data = new WindowOperationOutcome
            {
                Before = before,
            },
        };

    private static NativeWindowRect CalculateTarget(
        NativeWindowRect work,
        WindowLayout layout)
    {
        var halfWidth = work.Width / 2;
        var halfHeight = work.Height / 2;
        var thirdWidth = work.Width / 3;
        return layout switch
        {
            WindowLayout.Left => FromSize(
                work.Left,
                work.Top,
                halfWidth,
                work.Height),
            WindowLayout.Right => FromSize(
                work.Left + halfWidth,
                work.Top,
                work.Width - halfWidth,
                work.Height),
            WindowLayout.Maximize => work,
            WindowLayout.Bottom => FromSize(
                work.Left,
                work.Top + halfHeight,
                work.Width,
                work.Height - halfHeight),
            WindowLayout.TopLeft => FromSize(
                work.Left,
                work.Top,
                halfWidth,
                halfHeight),
            WindowLayout.TopRight => FromSize(
                work.Left + halfWidth,
                work.Top,
                work.Width - halfWidth,
                halfHeight),
            WindowLayout.BottomLeft => FromSize(
                work.Left,
                work.Top + halfHeight,
                halfWidth,
                work.Height - halfHeight),
            WindowLayout.BottomRight => FromSize(
                work.Left + halfWidth,
                work.Top + halfHeight,
                work.Width - halfWidth,
                work.Height - halfHeight),
            WindowLayout.ThirdLeft => FromSize(
                work.Left,
                work.Top,
                thirdWidth,
                work.Height),
            WindowLayout.ThirdRight => FromSize(
                work.Left + thirdWidth,
                work.Top,
                work.Width - thirdWidth,
                work.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    private static NativeWindowRect FromSize(
        int x,
        int y,
        int width,
        int height)
        => new(x, y, x + width, y + height);

    private static bool SnapshotEquals(
        WindowSnapshot expected,
        WindowSnapshot actual)
        => RectEquals(expected.Rect, actual.Rect)
            && expected.Topmost == actual.Topmost
            && expected.Placement.ShowCommand
                == actual.Placement.ShowCommand;

    private static bool RectEquals(
        NativeWindowRect left,
        NativeWindowRect right)
        => left.Left == right.Left
            && left.Top == right.Top
            && left.Right == right.Right
            && left.Bottom == right.Bottom;

    private static WindowDisplayState GetDisplayState(uint showCommand)
        => showCommand switch
        {
            SwShowMinimized or SwMinimize or SwShowMinNoActive or SwForceMinimize
                => WindowDisplayState.Minimized,
            SwShowMaximized => WindowDisplayState.Maximized,
            _ => WindowDisplayState.Normal,
        };

    private static string Win32Failure(string operation, int error)
        => error == 0
            ? $"{operation} failed."
            : $"{operation} failed with Win32 error {error}.";

    private sealed record WindowSnapshot(
        NativeWindowRect Rect,
        NativeWindowPlacement Placement,
        bool Topmost,
        WindowInfo Info);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window,
        StringBuilder text,
        int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);
}
