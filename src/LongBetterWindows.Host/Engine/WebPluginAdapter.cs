using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将 WebView2 插件适配为 ILongPlugin。
    /// WebView2 生命周期与插件生命周期保持一致。
    /// </summary>
    public class WebPluginAdapter : ILongPlugin, IHasMainUI, IPluginCommandHandler, IDisposable
    {
        private readonly WebPluginRuntime _runtime;
        private readonly string _pluginDir;
        private readonly string _entryPoint;
        private PluginWindowHost? _window;
        private Task<bool>? _runtimeInitialization;
        private bool _closingForStop;
        private bool _isEmbedded;

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public PluginState State { get; private set; } = PluginState.Loaded;

        public WebPluginAdapter(WebPluginRuntime runtime, string id, string name, string version, string pluginDir, string entryPoint)
        {
            _runtime = runtime;
            _pluginDir = pluginDir;
            _entryPoint = entryPoint;
            Id = id;
            Name = name;
            Version = version;
        }

        public void ShowMainUI()
            => _ = ShowMainUIAsync();

        private async Task ShowMainUIAsync()
        {
            EnsureWindowVisible();
            if (!await EnsureRuntimeInitializedAsync())
                _window?.Close();
        }

        private void EnsureWindowVisible()
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(EnsureWindowVisible);
                return;
            }

            var webView = _runtime.EnsureView();
            Log.Debug("[Web:{Id}] 准备呈现主界面: Presentation={Presentation}, MainWindow={MainWindowType}",
                Id,
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
                    Name,
                    webView,
                    async () =>
                    {
                        _isEmbedded = false;
                        await HostProvider.Instance.PluginStore.HandleWindowClosedAsync(Id);
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

        private void ShowDetachedWindow(System.Windows.FrameworkElement webView)
        {
            _window = new PluginWindowHost(Name, webView, _runtime.Manifest.Window)
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
                    await HostProvider.Instance.PluginStore.HandleWindowClosedAsync(Id);
            };
            window.Show();
        }

        public async Task<bool> InitializeAsync(IHostApi host)
        {
            await Task.CompletedTask;
            Log.Debug("[Web:{Id}] 已注册，WebView2 将在首次打开时初始化", Id);
            return true;
        }

        private Task<bool> EnsureRuntimeInitializedAsync()
            => _runtimeInitialization ??= InitializeRuntimeCoreAsync();

        private async Task<bool> InitializeRuntimeCoreAsync()
        {
            try
            {
                await _runtime.InitializeAsync();
                Log.Debug("[Web:{Id}] WebView2 延迟初始化完成", Id);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Web:{Id}] WebView2 初始化失败（需安装 WebView2 运行时）", Id);
                State = PluginState.Error;
                return false;
            }
        }

        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            Log.Information("[Web:{Id}] 已启动", Id);
            return Task.FromResult(true);
        }

        public async Task<bool> StopAsync()
        {
            await ReleaseWebResourcesAsync();
            State = PluginState.Stopped;
            Log.Information("[Web:{Id}] 窗口与 WebView2 资源已释放", Id);
            return true;
        }

        private async Task ReleaseWebResourcesAsync()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                void ReleaseOnUiThread()
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
                        _runtimeInitialization = null;
                    }
                    finally { _closingForStop = false; }
                }

                if (dispatcher.CheckAccess())
                    ReleaseOnUiThread();
                else
                    await dispatcher.InvokeAsync(ReleaseOnUiThread);
                return;
            }

            _runtime.Dispose();
            _runtimeInitialization = null;
        }

        public void Dispose()
        {
            ReleaseWebResourcesAsync().GetAwaiter().GetResult();
        }

        public async Task<Contracts.PluginCommandResult> ExecuteCommandAsync(
            Contracts.PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWindowVisible();
            if (!await EnsureRuntimeInitializedAsync())
                return Contracts.PluginCommandResult.Failure("插件界面初始化失败，请检查 WebView2 Runtime。");

            await _runtime.SendCommandAsync(invocation);
            return Contracts.PluginCommandResult.Success();
        }
    }
}
