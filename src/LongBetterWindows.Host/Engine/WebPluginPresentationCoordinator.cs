using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WebPluginPresentationCoordinator
    {
        private readonly WebPluginRuntime _runtime;
        private readonly string _pluginId;
        private readonly string _pluginName;
        private readonly Func<Task> _windowClosed;
        private PluginWindowHost? _window;
        private bool _closingForStop;
        private bool _isEmbedded;

        internal WebPluginPresentationCoordinator(
            WebPluginRuntime runtime,
            string pluginId,
            string pluginName,
            Func<Task> windowClosed)
        {
            _runtime = runtime;
            _pluginId = pluginId;
            _pluginName = pluginName;
            _windowClosed = windowClosed;
        }

        internal void EnsureVisible()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(EnsureVisible);
                return;
            }

            var webView = _runtime.EnsureView();
            Log.Debug("[Web:{Id}] 准备呈现主界面: Presentation={Presentation}, MainWindow={MainWindowType}",
                _pluginId,
                _runtime.Manifest.Lifecycle?.DefaultPresentation
                    ?? Contracts.PluginPresentationMode.Detached,
                System.Windows.Application.Current.MainWindow?.GetType().Name ?? "null");

            if (_isEmbedded
                && System.Windows.Application.Current.MainWindow is MainWindow embeddedOwner
                && embeddedOwner.IsHostingEmbedded(webView))
            {
                embeddedOwner.Activate();
                return;
            }

            if (_window?.IsVisible == true)
            {
                _window.Activate();
                return;
            }

            if (_runtime.Manifest.Lifecycle?.DefaultPresentation
                    == Contracts.PluginPresentationMode.Embedded
                && System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                _isEmbedded = true;
                mainWindow.ShowEmbeddedPlugin(
                    _pluginName,
                    webView,
                    async () =>
                    {
                        _isEmbedded = false;
                        await NotifyWindowClosedAsync();
                    },
                    () =>
                    {
                        _isEmbedded = false;
                        ShowDetachedWindow(webView);
                    });
                return;
            }

            ShowDetachedWindow(webView);
        }

        internal void CloseVisibleSurface()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(CloseVisibleSurface);
                return;
            }

            var webView = _runtime.WebView;
            if (_isEmbedded && webView is not null
                && System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.CloseEmbeddedPlugin(webView);
                _isEmbedded = false;
                _ = NotifyWindowClosedAsync();
            }

            _window?.Close();
        }

        internal async Task ReleaseAsync()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                if (dispatcher.CheckAccess())
                    ReleaseOnUiThread();
                else
                    await dispatcher.InvokeAsync(ReleaseOnUiThread);
                return;
            }

            _runtime.Dispose();
        }

        private void ShowDetachedWindow(System.Windows.FrameworkElement webView)
        {
            _window = new PluginWindowHost(_pluginName, webView, _runtime.Manifest.Window)
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            var window = _window;
            window.Closed += async (_, _) =>
            {
                window.DetachContent();
                if (ReferenceEquals(_window, window))
                    _window = null;
                if (!_closingForStop)
                    await NotifyWindowClosedAsync();
            };
            window.Show();
        }

        private void ReleaseOnUiThread()
        {
            _closingForStop = true;
            try
            {
                var webView = _runtime.WebView;
                if (_isEmbedded && webView is not null
                    && System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.CloseEmbeddedPlugin(webView);
                    _isEmbedded = false;
                }

                if (_window is not null)
                {
                    _window.DetachContent();
                    _window.Close();
                    _window = null;
                }

                _runtime.Dispose();
            }
            finally
            {
                _closingForStop = false;
            }
        }

        private async Task NotifyWindowClosedAsync()
        {
            try
            {
                await _windowClosed();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Web:{Id}] 窗口关闭回调失败", _pluginId);
            }
        }
    }
}
