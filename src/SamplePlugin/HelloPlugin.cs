using LongBetterWindows.Host.Core;
using Serilog;

namespace SamplePlugin;

public class HelloPlugin : ILongPlugin
{
    public string Id => "com.long.sample";
    public string Name => "示例插件";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        Log.Information("[示例插件] 初始化完成");
        return Task.FromResult(true);
    }

    public Task<bool> StartAsync()
    {
        State = PluginState.Running;
        Log.Information("[示例插件] 已启动");
        return Task.FromResult(true);
    }

    public Task<bool> StopAsync()
    {
        State = PluginState.Disabled;
        Log.Information("[示例插件] 已停止");
        return Task.FromResult(true);
    }
}
