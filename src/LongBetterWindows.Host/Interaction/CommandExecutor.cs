using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using Serilog;
using System.Diagnostics;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>将命令索引中的描述符解析为插件调用，并提供旧插件 UI 兼容路径。</summary>
    public sealed class CommandExecutor : IWorkflowCommandRunner
    {
        private readonly PluginRegistry _plugins;

        public CommandExecutor(PluginRegistry plugins)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        }

        public async Task<PluginCommandResult> ExecuteAsync(
            string commandKey,
            PluginCommandInvocation? invocation = null,
            CancellationToken cancellationToken = default)
        {
            var descriptor = _plugins.Commands.Get(commandKey);
            if (descriptor == null)
                return PluginCommandResult.Failure($"未找到命令: {commandKey}");

            var entry = _plugins.Get(descriptor.PluginId);
            if (entry == null)
                return PluginCommandResult.Failure($"插件未加载: {descriptor.PluginName}");

            invocation ??= new PluginCommandInvocation
            {
                CommandId = descriptor.Command.Id,
            };

            if (!string.Equals(
                    invocation.CommandId,
                    descriptor.Command.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return PluginCommandResult.Failure("调用上下文中的 command_id 与目标命令不一致。");
            }

            if (invocation.InputType != AcceptedInputType.None
                && !descriptor.Command.AcceptedInputs.Contains(invocation.InputType))
            {
                return PluginCommandResult.Failure(
                    $"命令不接受 {invocation.InputType} 类型的输入。");
            }

            var argumentValidation = PluginCommandArgumentValidator.Validate(
                descriptor.Command.ArgumentSchema,
                invocation.Arguments);
            if (!argumentValidation.IsSuccess)
            {
                return PluginCommandResult.Failure(
                    "命令参数无效：" + string.Join(" ", argumentValidation.Issues));
            }
            invocation = new PluginCommandInvocation
            {
                CommandId = invocation.CommandId,
                InputType = invocation.InputType,
                Text = invocation.Text,
                Paths = invocation.Paths,
                ImagePng = invocation.ImagePng,
                Arguments = argumentValidation.Arguments,
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (entry.State != PluginState.Running)
                {
                    var started = await _plugins.StartPluginAsync(
                        entry.Id,
                        persistAutoStart: false);
                    if (!started && entry.State != PluginState.Running)
                    {
                        _plugins.RuntimeHealth.RecordFailure(
                            entry.Id,
                            stopwatch.Elapsed,
                            PluginRuntimeFailureKind.StartFailed);
                        return PluginCommandResult.Failure($"插件启动失败: {entry.DisplayName}");
                    }
                }

                using (PluginAccessContext.Enter(entry.Id))
                {
                    if (entry.Instance is IPluginCommandHandler handler)
                    {
                        var result = await handler.ExecuteCommandAsync(invocation, cancellationToken);
                        if (result.IsSuccess)
                            _plugins.RuntimeHealth.RecordSuccess(entry.Id, stopwatch.Elapsed);
                        else
                            _plugins.RuntimeHealth.RecordFailure(entry.Id, stopwatch.Elapsed);
                        return result;
                    }

                    if (entry.Instance is IHasMainUI mainUi)
                    {
                        mainUi.ShowMainUI();
                        _plugins.RuntimeHealth.RecordSuccess(entry.Id, stopwatch.Elapsed);
                        return PluginCommandResult.Success();
                    }
                }

                _plugins.RuntimeHealth.RecordFailure(entry.Id, stopwatch.Elapsed);
                return PluginCommandResult.Failure(
                    $"插件尚未实现命令执行接口: {entry.DisplayName}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _plugins.RuntimeHealth.RecordCancellation(entry.Id, stopwatch.Elapsed);
                return PluginCommandResult.Failure("命令已取消。");
            }
            catch (Exception ex)
            {
                _plugins.RuntimeHealth.RecordException(entry.Id, stopwatch.Elapsed);
                Log.Error(ex, "执行插件命令 {CommandKey} 失败", commandKey);
                return PluginCommandResult.Failure($"执行失败: {ex.Message}");
            }
        }
    }
}
