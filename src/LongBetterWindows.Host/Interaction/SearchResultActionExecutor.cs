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
        private readonly Func<string, string>? _localize;

        public SearchResultActionExecutor(
            PluginRegistry plugins,
            Func<
                string,
                string?,
                CancellationToken,
                Task<PluginCommandResult>>? workflowReviewLauncher = null,
            Func<string, string>? localize = null)
        {
            _plugins = plugins;
            _commands = new CommandExecutor(plugins);
            _workflowReviewLauncher = workflowReviewLauncher;
            _localize = localize;
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
                        return Failure(
                            "search.error.commandExpired",
                            "结果对应的命令已经失效。");
                    var invocation = action.Invocation
                        ?? CommandInvocationFactory.Create(descriptor, context);
                    return await _commands.ExecuteAsync(
                        descriptor.Key, invocation, cancellationToken);

                case SearchActionKind.OpenWorkflowReview:
                    if (_workflowReviewLauncher is null)
                        return Failure(
                            "search.error.workflowReviewUnavailable",
                            "组合动作审查入口当前不可用。");
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
                        return Failure(
                            "search.error.folderUnavailable",
                            "无法确定结果所在文件夹。");
                    return FromHostResponse(
                        await ServicesInitializer.ShellExecute.OpenFolderAsync(folder));

                case SearchActionKind.OpenUri:
                    if (!Uri.TryCreate(action.Target, UriKind.Absolute, out var uri)
                        || !IsAllowedHostUriScheme(uri.Scheme))
                        return Failure(
                            "search.error.invalidUri",
                            "不支持或无效的链接地址。");
                    return FromHostResponse(
                        await ServicesInitializer.ShellExecute.OpenUrlAsync(action.Target));

                case SearchActionKind.CopyText:
                    return FromHostResponse(
                        await ServicesInitializer.Clipboard.SetTextAsync(action.Target),
                        Text("search.result.copied", "已复制到剪贴板。"),
                        keepOpen: true);

                default:
                    return Failure(
                        "search.error.actionUnsupported",
                        "该动作不能直接执行。");
            }
        }

        private PluginCommandResult FromHostResponse(
            HostApiResponse response,
            string? successMessage = null,
            bool keepOpen = false)
            => response.IsSuccess
                ? PluginCommandResult.Success(successMessage, keepOpen)
                : PluginCommandResult.Failure(
                    response.ErrorMessage
                    ?? Text("search.error.operationFailed", "操作失败。"));

        private PluginCommandResult Failure(string key, string fallback)
            => PluginCommandResult.Failure(Text(key, fallback));

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

        internal static bool IsAllowedHostUriScheme(string scheme)
            => scheme.Equals("ms-settings", StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }
}
