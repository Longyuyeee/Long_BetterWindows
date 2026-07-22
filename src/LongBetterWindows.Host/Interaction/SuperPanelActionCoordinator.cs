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

        public SuperPanelActionCoordinator(
            PluginRegistry plugins,
            SearchPreferenceService preferences,
            Func<string, CancellationToken, Task<PluginCommandResult>>? workflowReviewLauncher = null)
        {
            _plugins = plugins;
            _executor = new SearchResultActionExecutor(plugins, workflowReviewLauncher);
            _preferences = preferences;
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
                        Message: "操作已失效");
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
                result.Message ?? (result.IsSuccess ? "操作已完成" : "操作失败"));
        }
    }
}
