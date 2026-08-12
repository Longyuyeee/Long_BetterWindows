using System.Windows;

namespace LongBetterWindows.Host.Helpers;

internal static class DialogOwnerResolver
{
    internal static Window Resolve(DependencyObject origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var containingWindow = Window.GetWindow(origin);
        if (containingWindow is { IsVisible: true })
            return containingWindow;

        var activeWindow = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsVisible);
        if (activeWindow is not null)
            return activeWindow;

        if (Application.Current.MainWindow is { IsVisible: true } mainWindow)
            return mainWindow;

        throw new InvalidOperationException(
            "A visible owner window is required before opening a system dialog.");
    }
}
