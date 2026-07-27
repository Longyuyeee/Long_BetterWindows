namespace LongBetterWindows.Host.Core
{
    public interface ILongPlugin
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        PluginState State { get; }

        Task<bool> InitializeAsync(IHostApi host);
        Task<bool> StartAsync();
        Task<bool> StopAsync();
    }

    /// <summary>
    /// 可选的后台生命周期扩展。未实现时，宿主仍可保留插件实例并切换注册表状态。
    /// </summary>
    public interface IPluginBackgroundLifecycle
    {
        Task<bool> EnterBackgroundAsync();
        Task<bool> ResumeAsync();
    }

    /// <summary>
    /// 可选的资源释放扩展。插件停止时由宿主调用，用于释放事件订阅、计时器等
    /// 不属于宿主能力服务的资源。重复调用必须安全。
    /// </summary>
    public interface IPluginResourceLifecycle
    {
        Task ReleaseResourcesAsync();
    }

    /// <summary>
    /// 可选的插件语言生命周期扩展。仅声明 localization 的插件会收到通知；
    /// 回调不得重启插件或清空运行状态。
    /// </summary>
    public interface IPluginLanguageLifecycle
    {
        Task OnLanguageChangedAsync(
            Contracts.PluginLanguageContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 后台伴生组件可通过该事件请求宿主打开同一插件的主界面。
    /// 典型用途是全局热键；事件不得直接创建独立窗口。
    /// </summary>
    public interface IPluginOpenRequestSource
    {
        event Action? OpenRequested;
    }
}
