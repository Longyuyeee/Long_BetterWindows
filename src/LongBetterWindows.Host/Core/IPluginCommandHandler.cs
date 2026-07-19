using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Core
{
    /// <summary>
    /// 可选插件接口。多命令插件或需要消费上下文输入的插件应实现此接口。
    /// 单入口 UI 插件可由宿主通过 IHasMainUI 兼容执行。
    /// </summary>
    public interface IPluginCommandHandler
    {
        Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default);
    }
}
