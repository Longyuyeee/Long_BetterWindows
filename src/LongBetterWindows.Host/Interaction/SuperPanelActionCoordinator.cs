using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record SuperPanelActionOutcome(
        SearchActionKind Kind,
        bool IsSuccess,
        bool KeepPanelOpen,
        string Message,
        string? ContinuationQuery = null);

    public sealed class SuperPanelActionCoordinator
    {
        private readonly PluginRegistry _plugins;
        private readonly SearchResultActionExecutor _executor;
        private readonly SearchPreferenceService _preferences;
        private readonly Func<string, string>? _localize;

        public SuperPanelActionCoordinator(
            PluginRegistry plugins,
            SearchPreferenceService preferences,
            Func<
                string,
                string?,
                CancellationToken,
                Task<PluginCommandResult>>? workflowReviewLauncher = null,
            Func<string, string>? localize = null)
        {
            _plugins = plugins;
            _executor = new SearchResultActionExecutor(
                plugins,
                workflowReviewLauncher,
                localize);
            _preferences = preferences;
            _localize = localize;
        }

        public async Task<SuperPanelActionOutcome> ExecuteAsync(
            SearchResultItem selected,
            SearchResultAction action,
            ContextSnapshot context,
            Func<Task>? beforeCommandExecution = null,
            CancellationToken cancellationToken = default)
        {
            if (action.Kind == SearchActionKind.ContinueSearch)
            {
                return new SuperPanelActionOutcome(
                    action.Kind,
                    IsSuccess: true,
                    KeepPanelOpen: false,
                    Message: string.Empty,
                    ContinuationQuery: action.Target);
            }

            if (action.Kind == SearchActionKind.ExecuteCommand)
            {
                if (_plugins.Commands.Get(action.Target) is null)
                {
                    return new SuperPanelActionOutcome(
                        action.Kind,
                        IsSuccess: false,
                        KeepPanelOpen: true,
                        Message: Text(
                            "search.error.actionExpired",
                            "操作已失效"));
                }

            }

            if (action.Kind is SearchActionKind.ExecuteCommand
                or SearchActionKind.OpenWorkflowReview
                && beforeCommandExecution is not null)
                await beforeCommandExecution();

            PluginCommandResult result = await _executor.ExecuteAsync(
                action, context, cancellationToken);
            if (result.IsSuccess)
                await _preferences.RecordUseAsync(selected.Id);

            return new SuperPanelActionOutcome(
                action.Kind,
                result.IsSuccess,
                result.KeepPaletteOpen,
                result.Message ?? (result.IsSuccess
                    ? Text("search.result.completed", "操作已完成")
                    : Text("search.error.operationFailed", "操作失败")));
        }

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
