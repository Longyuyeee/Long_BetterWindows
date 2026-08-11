using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LongBetterWindows.Host.Automation;

internal enum QualityWindowAction
{
    ExecutePrimary = 1,
    ExecuteSecondary = 2,
    Dismiss = 3,
    SelectDeterministicResult = 4,
    SelectFocusProbeCommand = 5,
    SelectManagementWorkspace = 6,
    SelectMarketplaceWorkspace = 7,
    SelectSettingsWorkspace = 8,
}

internal sealed class QualityWindowAutomation : IDisposable
{
    private const string MessageName =
        "LongBetterWindows.Quality.WindowAction.v1";
    private static readonly int MessageId =
        checked((int)RegisterWindowMessage(MessageName));

    private readonly Window _window;
    private readonly Func<QualityWindowAction, bool> _dispatch;
    private HwndSource? _source;

    private QualityWindowAutomation(
        Window window,
        Func<QualityWindowAction, bool> dispatch)
    {
        _window = window;
        _dispatch = dispatch;
        window.SourceInitialized += Window_SourceInitialized;
        window.Closed += Window_Closed;
    }

    internal static QualityWindowAutomation? Attach(
        Window window,
        Func<QualityWindowAction, bool> dispatch)
        => App.EnableWindowAutomationForQuality
            ? new QualityWindowAutomation(window, dispatch)
            : null;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WindowHook);
    }

    private nint WindowHook(
        nint window,
        int message,
        nint action,
        nint parameter,
        ref bool handled)
    {
        if (message != MessageId
            || !Enum.IsDefined(typeof(QualityWindowAction), (int)action))
        {
            return nint.Zero;
        }

        handled = true;
        return _dispatch((QualityWindowAction)(int)action)
            ? new nint(1)
            : nint.Zero;
    }

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        _source?.RemoveHook(WindowHook);
        _source = null;
        _window.SourceInitialized -= Window_SourceInitialized;
        _window.Closed -= Window_Closed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string messageName);
}
