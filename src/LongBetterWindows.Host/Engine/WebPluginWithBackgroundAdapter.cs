using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将按需 Web UI 与轻量原生后台组件组合为一个插件生命周期。
    /// 后台组件不创建窗口，WebView2 仍只在首次打开或执行命令时初始化。
    /// </summary>
    public sealed class WebPluginWithBackgroundAdapter :
        ILongPlugin,
        IHasMainUI,
        IPluginCommandHandler,
        IPluginLanguageLifecycle,
        IPluginBackgroundLifecycle,
        IPluginResourceLifecycle,
        IDisposable
    {
        private readonly WebPluginAdapter _web;
        private readonly ILongPlugin _background;
        private readonly IPluginOpenRequestSource? _openRequestSource;

        public WebPluginWithBackgroundAdapter(
            WebPluginAdapter web,
            ILongPlugin background)
        {
            _web = web;
            _background = background;
            _openRequestSource = background as IPluginOpenRequestSource;
            if (_openRequestSource is not null)
                _openRequestSource.OpenRequested += ShowMainUI;
        }

        public string Id => _web.Id;
        public string Name => _web.Name;
        public string Version => _web.Version;
        public PluginState State { get; private set; } = PluginState.Loaded;

        public async Task<bool> InitializeAsync(IHostApi host)
        {
            if (!await _web.InitializeAsync(host))
                return false;
            if (!await _background.InitializeAsync(host))
                return false;
            return true;
        }

        public async Task<bool> StartAsync()
        {
            if (!await _background.StartAsync())
                return false;
            if (!await _web.StartAsync())
            {
                await _background.StopAsync();
                return false;
            }

            State = PluginState.Running;
            return true;
        }

        public async Task<bool> StopAsync()
        {
            var backgroundStopped = await _background.StopAsync();
            var webStopped = await _web.StopAsync();
            if (!backgroundStopped || !webStopped)
                return false;

            State = PluginState.Stopped;
            return true;
        }

        public void ShowMainUI() => _web.ShowMainUI();

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
            => _web.ExecuteCommandAsync(invocation, cancellationToken);

        public async Task OnLanguageChangedAsync(
            PluginLanguageContext context,
            CancellationToken cancellationToken = default)
        {
            await _web.OnLanguageChangedAsync(context, cancellationToken);
            if (_background is IPluginLanguageLifecycle localized)
                await localized.OnLanguageChangedAsync(context, cancellationToken);
        }

        public Task<bool> EnterBackgroundAsync()
        {
            State = PluginState.Background;
            return Task.FromResult(true);
        }

        public Task<bool> ResumeAsync()
        {
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public async Task ReleaseResourcesAsync()
        {
            if (_background is IPluginResourceLifecycle resources)
                await resources.ReleaseResourcesAsync();
        }

        public void Dispose()
        {
            if (_openRequestSource is not null)
                _openRequestSource.OpenRequested -= ShowMainUI;
            if (_background is IDisposable disposable)
                disposable.Dispose();
            _web.Dispose();
        }
    }
}
