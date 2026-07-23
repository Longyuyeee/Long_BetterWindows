using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class SearchResultActionExecutor
    {
        private readonly PluginRegistry _plugins;
        private readonly CommandExecutor _commands;
        private readonly Func<
            string,
            string?,
            CancellationToken,
            Task<PluginCommandResult>>? _workflowReviewLauncher;

        public SearchResultActionExecutor(
            PluginRegistry plugins,
            Func<
                string,
                string?,
                CancellationToken,
                Task<PluginCommandResult>>? workflowReviewLauncher = null)
        {
            _plugins = plugins;
            _commands = new CommandExecutor(plugins);
            _workflowReviewLauncher = workflowReviewLauncher;
        }

        public async Task<PluginCommandResult> ExecuteAsync(
            SearchResultAction action,
            ContextSnapshot context,
            CancellationToken cancellationToken = default)
        {
            switch (action.Kind)
            {
                case SearchActionKind.ExecuteCommand:
                    var descriptor = _plugins.Commands.Get(action.Target);
                    if (descriptor is null)
                        return PluginCommandResult.Failure("结果对应的命令已经失效。");
                    var invocation = action.Invocation
                        ?? CommandInvocationFactory.Create(descriptor, context);
                    return await _commands.ExecuteAsync(
                        descriptor.Key, invocation, cancellationToken);

                case SearchActionKind.OpenWorkflowReview:
                    if (_workflowReviewLauncher is null)
                        return PluginCommandResult.Failure("组合动作审查入口当前不可用。");
                    return await _workflowReviewLauncher(
                        action.Target,
                        action.ExpectedStateFingerprint,
                        cancellationToken);

                case SearchActionKind.OpenPath:
                    return FromHostResponse(
                        await ServicesInitializer.ShellExecute.OpenWithDefaultAsync(action.Target));

                case SearchActionKind.OpenContainingFolder:
                    var folder = Directory.Exists(action.Target)
                        ? action.Target
                        : Path.GetDirectoryName(action.Target);
                    if (string.IsNullOrWhiteSpace(folder))
                        return PluginCommandResult.Failure("无法确定结果所在文件夹。");
                    return FromHostResponse(
                        await ServicesInitializer.ShellExecute.OpenFolderAsync(folder));

                case SearchActionKind.OpenUri:
                    if (!Uri.TryCreate(action.Target, UriKind.Absolute, out var uri)
                        || !IsAllowedHostUriScheme(uri.Scheme))
                        return PluginCommandResult.Failure("不支持或无效的链接地址。");
                    return FromHostResponse(
                        await ServicesInitializer.ShellExecute.OpenUrlAsync(action.Target));

                case SearchActionKind.CopyText:
                    return FromHostResponse(
                        await ServicesInitializer.Clipboard.SetTextAsync(action.Target),
                        "已复制到剪贴板。",
                        keepOpen: true);

                default:
                    return PluginCommandResult.Failure("该动作不能直接执行。");
            }
        }

        private static PluginCommandResult FromHostResponse(
            HostApiResponse response,
            string? successMessage = null,
            bool keepOpen = false)
            => response.IsSuccess
                ? PluginCommandResult.Success(successMessage, keepOpen)
                : PluginCommandResult.Failure(response.ErrorMessage ?? "操作失败。");

        internal static bool IsAllowedHostUriScheme(string scheme)
            => scheme.Equals("ms-settings", StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }
}
