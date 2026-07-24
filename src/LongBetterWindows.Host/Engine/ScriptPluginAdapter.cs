using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 将 .csx 脚本插件适配为 ILongPlugin 接口。
    /// 使脚本插件与 DLL 插件使用相同的注册、启停、权限流程。
    /// </summary>
    public class ScriptPluginAdapter : ILongPlugin, IPluginLanguageLifecycle
    {
        private readonly ScriptGlobals _globals;

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public PluginState State { get; private set; } = PluginState.Loaded;

        public ScriptPluginAdapter(ScriptGlobals globals, string id, string name, string version)
        {
            _globals = globals;
            Id = id;
            Name = name;
            Version = version;
        }

        public async Task<bool> InitializeAsync(IHostApi host)
        {
            // 脚本插件在 ScriptPluginLoader.LoadAsync 中已执行全局代码
            // 这里只是标记初始化完成
            Log.Debug("[Script:{Id}] 初始化完成", Id);
            return await Task.FromResult(true);
        }

        public async Task<bool> StartAsync()
        {
            if (_globals.Start != null)
            {
                try
                {
                    await _globals.Start();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Script:{Id}] Start 委托执行失败", Id);
                    State = PluginState.Error;
                    return false;
                }
            }

            State = PluginState.Running;
            Log.Information("[Script:{Id}] 已启动", Id);
            return true;
        }

        public async Task<bool> StopAsync()
        {
            if (_globals.Stop != null)
            {
                try
                {
                    await _globals.Stop();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Script:{Id}] Stop 委托执行失败", Id);
                }
            }

            State = PluginState.Stopped;
            Log.Information("[Script:{Id}] 已停止", Id);
            return true;
        }

        public async Task OnLanguageChangedAsync(
            Contracts.PluginLanguageContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_globals.LanguageChanged is not null)
                await _globals.LanguageChanged(context);
        }
    }
}
