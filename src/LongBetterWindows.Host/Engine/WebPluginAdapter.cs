using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将 WebView2 插件适配为 ILongPlugin。
    /// WebView2 生命周期与插件生命周期保持一致。
    /// </summary>
    public class WebPluginAdapter :
        ILongPlugin,
        IHasMainUI,
        IPluginCommandHandler,
        IPluginLanguageLifecycle,
        IDisposable
    {
        private readonly WebPluginRuntime _runtime;
        private readonly WebPluginPresentationCoordinator _presentation;
        private Task<bool>? _runtimeInitialization;
        public string Id { get; }
        public string Name { get; private set; }
        public string Version { get; }
        public PluginState State { get; private set; } = PluginState.Loaded;

        public WebPluginAdapter(WebPluginRuntime runtime, string id, string name, string version, string pluginDir, string entryPoint)
        {
            _runtime = runtime;
            Id = id;
            Name = name;
            Version = version;
            _presentation = new WebPluginPresentationCoordinator(
                runtime, id, name,
                () => HostProvider.Instance.PluginStore.HandleWindowClosedAsync(id));
        }

        public void ShowMainUI()
            => _ = ShowMainUIAsync();
        private async Task ShowMainUIAsync()
        {
            EnsureWindowVisible();
            if (!await EnsureRuntimeInitializedAsync())
                _presentation.CloseVisibleSurface();
        }

        private void EnsureWindowVisible() => _presentation.EnsureVisible();
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
                if (!await _runtime.InitializeAsync())
                {
                    State = PluginState.Error;
                    return false;
                }
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
            await _presentation.ReleaseAsync();
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

            return await _runtime.SendCommandAsync(invocation, cancellationToken);
        }

        public async Task OnLanguageChangedAsync(
            Contracts.PluginLanguageContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Resources.TryGetValue("plugin.name", out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                Name = name;
                _presentation.UpdatePluginName(name);
            }
            await _runtime.NotifyLanguageChangedAsync(context);
        }
    }
}
