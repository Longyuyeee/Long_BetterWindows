using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
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
        private PluginWorkspaceSession? _session;
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
            var session = GetOrCreateSession();

            if (_isEmbedded
                && System.Windows.Application.Current.MainWindow is MainWindow embeddedOwner
                && embeddedOwner.IsHostingPluginRuntime(webView))
            {
                session.ShowEmbedded();
                embeddedOwner.Activate();
                return;
            }

            if (_window?.IsVisible == true)
            {
                session.ShowDetached();
                _window.Activate();
                return;
            }

            if (session.State.LastVisiblePlacement
                    == PluginWorkspacePlacement.Embedded
                && System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                ShowEmbedded(mainWindow, webView);
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
                _session?.Hide();
                mainWindow.ReleasePluginRuntimeView(webView);
                _isEmbedded = false;
                _ = NotifyWindowClosedAsync();
                if (_session is not null)
                {
                    _ = mainWindow.RemovePluginRuntimeModuleAsync(
                        _session.State.SessionId,
                        webView);
                }
            }

            if (_window is not null)
                _session?.Hide();
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
            EndSession();
        }

        private PluginWorkspaceSession GetOrCreateSession()
        {
            if (_session is not null && !_session.State.IsEnded)
                return _session;

            var preferredPlacement =
                _runtime.Manifest.Lifecycle?.DefaultPresentation
                    == Contracts.PluginPresentationMode.Embedded
                    ? PluginWorkspacePlacement.Embedded
                    : PluginWorkspacePlacement.Detached;
            _session = ServicesInitializer.PluginSessions.GetOrCreate(
                _pluginId,
                preferredPlacement);
            return _session;
        }

        private async void ShowEmbedded(
            MainWindow mainWindow,
            System.Windows.FrameworkElement webView)
        {
            var session = GetOrCreateSession();
            try
            {
                var error = await mainWindow.ShowPluginRuntimeModuleAsync(
                    _pluginId,
                    session.State.SessionId,
                    _pluginName,
                    webView,
                    () =>
                    {
                        _isEmbedded = true;
                        session.ShowEmbedded();
                    },
                    () =>
                    {
                        _isEmbedded = false;
                        session.Hide();
                    },
                    () => CloseWorkspaceViewAsync(session),
                    () =>
                    {
                        _isEmbedded = false;
                        session.ShowDetached();
                        ShowDetachedWindow(webView);
                    },
                    EndRunAsync);
                if (error is null)
                    return;

                _isEmbedded = false;
                session.Hide();
                Log.Warning(
                    "Plugin runtime module {PluginId}:{SessionId} could not open: {Error}",
                    _pluginId,
                    session.State.SessionId,
                    error);
            }
            catch (Exception exception)
            {
                _isEmbedded = false;
                session.Hide();
                Log.Error(
                    exception,
                    "Plugin runtime module {PluginId}:{SessionId} navigation failed",
                    _pluginId,
                    session.State.SessionId);
            }
        }

        private async Task CloseWorkspaceViewAsync(
            PluginWorkspaceSession session)
        {
            _isEmbedded = false;
            session.Hide();
            if (_window is not null)
            {
                _window.Close();
                return;
            }
            await NotifyWindowClosedAsync();
        }

        private void ShowDetachedWindow(System.Windows.FrameworkElement webView)
        {
            var session = GetOrCreateSession();
            session.ShowDetached();
            _window = new PluginWindowHost(
                _pluginId,
                _pluginName,
                webView,
                _runtime.Manifest.Window,
                session.State.SessionId,
                EndRunAsync);
            _window.SetReturnTarget(System.Windows.Application.Current.MainWindow);
            var window = _window;
            window.Closed += async (_, _) =>
            {
                window.DetachContent();
                if (ReferenceEquals(_window, window))
                    _window = null;
                var mainWindow =
                    System.Windows.Application.Current.MainWindow as MainWindow;
                switch (PluginSurfaceCloseRouter.Route(
                    _closingForStop,
                    window.ReturnRequested,
                    mainWindow is not null))
                {
                    case PluginSurfaceCloseAction.ReturnToEmbedded:
                        ShowEmbedded(mainWindow!, webView);
                        break;
                    case PluginSurfaceCloseAction.HideAndApplyLifecycle:
                        session.Hide();
                        await NotifyWindowClosedAsync();
                        if (mainWindow is not null)
                        {
                            await mainWindow.RemovePluginRuntimeModuleAsync(
                                session.State.SessionId,
                                webView);
                        }
                        break;
                }
            };
            window.Show();
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                _ = RegisterDetachedWorkspaceModuleAsync(
                    mainWindow,
                    webView,
                    session);
            }
        }

        private async Task RegisterDetachedWorkspaceModuleAsync(
            MainWindow mainWindow,
            System.Windows.FrameworkElement webView,
            PluginWorkspaceSession session)
        {
            try
            {
                var error = await mainWindow.ShowPluginRuntimeModuleAsync(
                    _pluginId,
                    session.State.SessionId,
                    _pluginName,
                    webView,
                    () =>
                    {
                        _isEmbedded = true;
                        session.ShowEmbedded();
                    },
                    () =>
                    {
                        if (_window is null)
                            session.Hide();
                        _isEmbedded = false;
                    },
                    () => CloseWorkspaceViewAsync(session),
                    () => { },
                    EndRunAsync,
                    isDetached: true);
                if (error is not null)
                {
                    Log.Warning(
                        "Detached plugin runtime module {PluginId}:{SessionId} could not open: {Error}",
                        _pluginId,
                        session.State.SessionId,
                        error);
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Detached plugin runtime module {PluginId}:{SessionId} navigation failed",
                    _pluginId,
                    session.State.SessionId);
            }
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
                    mainWindow.ReleasePluginRuntimeView(webView);
                    _isEmbedded = false;
                }

                if (_window is not null)
                {
                    _window.DetachContent();
                    _window.Close();
                    _window = null;
                }

                _runtime.Dispose();
                EndSession();
            }
            finally
            {
                _closingForStop = false;
            }
        }

        private async Task EndRunAsync()
        {
            if (!await HostProvider.Instance.PluginStore.StopPluginAsync(
                    _pluginId,
                    persistAutoStart: false))
            {
                throw new InvalidOperationException(
                    $"Plugin '{_pluginId}' could not be stopped.");
            }
        }

        private void EndSession()
        {
            if (_session is null)
                return;
            ServicesInitializer.PluginSessions.End(_session.State.SessionId);
            _session = null;
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
