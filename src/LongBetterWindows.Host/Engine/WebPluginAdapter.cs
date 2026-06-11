using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将 WebView2 插件适配为 ILongPlugin。
    /// WebView2 生命周期与插件生命周期保持一致。
    /// </summary>
    public class WebPluginAdapter : ILongPlugin, IDisposable
    {
        private readonly WebPluginRuntime _runtime;

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public PluginState State { get; private set; } = PluginState.Loaded;

        public WebPluginAdapter(WebPluginRuntime runtime, string id, string name, string version)
        {
            _runtime = runtime;
            Id = id;
            Name = name;
            Version = version;
        }

        public async Task<bool> InitializeAsync(IHostApi host)
        {
            try
            {
                await _runtime.InitializeAsync();
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
            _runtime.WebView.Dispose();
        }
    }
}
