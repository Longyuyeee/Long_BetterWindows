using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将 WebView2 插件适配为 ILongPlugin。
    /// WebView2 生命周期与插件生命周期保持一致。
    /// </summary>
    public class WebPluginAdapter : ILongPlugin, IHasMainUI, IDisposable
    {
        private readonly WebPluginRuntime _runtime;
        private readonly string _pluginDir;
        private readonly string _entryPoint;

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
        {
            var wv = _runtime.WebView;
            if (wv == null)
            {
                Log.Warning("[Web:{Id}] WebView 尚未初始化，无法打开 UI", Id);
                return;
            }
            _runtime.DetachFromRuntimeHost();
            var w = new System.Windows.Window
            {
                Title = Name,
                Width = 480, Height = 520,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                Content = wv,
            };
            w.Show();
        }

        public async Task<bool> InitializeAsync(IHostApi host)
        {
            try
            {
                var initialized = await _runtime.InitializeAsync();
                if (!initialized)
                {
                    State = PluginState.Error;
                    return false;
                }
                Log.Debug("[Web:{Id}] WebView2 初始化完成", Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Web:{Id}] WebView2 初始化失败（需安装 WebView2 运行时）", Id);
                State = PluginState.Error;
                return false;
            }

            return true;
        }

        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            Log.Information("[Web:{Id}] 已启动", Id);
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Disabled;
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            _runtime.WebView?.Dispose();
        }
    }
}
