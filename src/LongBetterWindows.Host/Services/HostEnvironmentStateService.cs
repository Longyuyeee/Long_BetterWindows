using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host.Services;

internal enum HostPowerTransition
{
    Suspended,
    ResumedFromSuspend,
    ResumedAutomatically,
}

internal sealed class HostEnvironmentStateService : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const int WmPowerBroadcast = 0x0218;
    private const int WmWtsSessionChange = 0x02B1;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;
    private const uint NotifyForThisSession = 0;

    private HwndSource? _source;
    private Window? _mainWindow;
    private bool _sessionAvailable = true;
    private bool _powerAvailable = true;

    internal static HostEnvironmentStateService Current { get; } = new();
    internal bool IsInteractionAvailable
        => _sessionAvailable && _powerAvailable;
    internal event Action<bool>? InteractionAvailabilityChanged;
    internal event Action<HostPowerTransition>? PowerTransitionObserved;
    internal event Action? DisplayTopologyChanged;

    internal void Attach(Window mainWindow)
    {
        if (_source is not null)
            return;
        _mainWindow = mainWindow;
        var handle = new WindowInteropHelper(mainWindow).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException(
                "The host window message source is unavailable.");
        _source.AddHook(WndProc);
        if (!WTSRegisterSessionNotification(handle, NotifyForThisSession))
        {
            Log.Warning(
                "Host session notifications could not be registered: {Error}",
                Marshal.GetLastWin32Error());
        }
    }

    internal void SetInteractionAvailableForQuality(bool available)
    {
        _sessionAvailable = available;
        _powerAvailable = available;
        PublishAvailability();
    }

    internal void RefreshDisplayForQuality() => ConstrainMainWindow();

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        switch (message)
        {
            case WmWtsSessionChange when wParam.ToInt32() == WtsSessionLock:
                SetSessionAvailable(false);
                break;
            case WmWtsSessionChange when wParam.ToInt32() == WtsSessionUnlock:
                SetSessionAvailable(true);
                break;
            case WmPowerBroadcast:
                ApplyPowerTransition(wParam.ToInt32());
                break;
            case WmDisplayChange:
                ConstrainMainWindow();
                PublishDisplayTopologyChanged();
                break;
        }
        return IntPtr.Zero;
    }

    private void SetSessionAvailable(bool available)
    {
        if (_sessionAvailable == available)
            return;
        _sessionAvailable = available;
        PublishAvailability();
    }

    private void SetPowerAvailable(bool available)
    {
        if (_powerAvailable == available)
            return;
        _powerAvailable = available;
        PublishAvailability();
    }

    private void ApplyPowerTransition(int notification)
    {
        var transition = ParsePowerTransitionForQuality(notification);
        if (transition is null)
            return;

        SetPowerAvailable(transition != HostPowerTransition.Suspended);
        var handlers = PowerTransitionObserved?.GetInvocationList()
            .Cast<Action<HostPowerTransition>>() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(transition.Value);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "A host power transition subscriber failed");
            }
        }
    }

    internal static HostPowerTransition? ParsePowerTransitionForQuality(
        int notification)
        => notification switch
        {
            PbtApmSuspend => HostPowerTransition.Suspended,
            PbtApmResumeSuspend => HostPowerTransition.ResumedFromSuspend,
            PbtApmResumeAutomatic => HostPowerTransition.ResumedAutomatically,
            _ => null,
        };

    private void PublishAvailability()
    {
        var available = IsInteractionAvailable;
        Log.Debug("Host interaction availability changed: {Available}", available);
        var handlers = InteractionAvailabilityChanged?.GetInvocationList()
            .Cast<Action<bool>>() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler(available);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "A host interaction availability subscriber failed");
            }
        }
    }

    private void PublishDisplayTopologyChanged()
    {
        var handlers = DisplayTopologyChanged?.GetInvocationList()
            .Cast<Action>() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "A host display topology subscriber failed");
            }
        }
    }

    private void ConstrainMainWindow()
    {
        if (_mainWindow is null || !_mainWindow.IsLoaded)
            return;
        _ = _mainWindow.Dispatcher.BeginInvoke(() =>
            PluginWindowPlacement.TryConstrainToNearestWorkArea(_mainWindow));
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _ = WTSUnRegisterSessionNotification(_source.Handle);
            _source.RemoveHook(WndProc);
        }
        _source = null;
        _mainWindow = null;
        _sessionAvailable = true;
        _powerAvailable = true;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(
        IntPtr window,
        uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
}
