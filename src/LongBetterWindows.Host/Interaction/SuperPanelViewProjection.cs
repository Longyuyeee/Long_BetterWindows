namespace LongBetterWindows.Host.Interaction
{
    internal static class SuperPanelViewProjection
    {
        public static SuperPanelContextViewState ProjectContext(
            SuperPanelContextUpdate update)
        {
            var hasItems = !update.IsLoading && update.Snapshot.Items.Count > 0;
            return new SuperPanelContextViewState(
                update.IsLoading ? null : update.Snapshot.Items,
                hasItems,
                update.IsLoading
                    ? "\u6b63\u5728\u8bfb\u53d6\u5f53\u524d\u4e0a\u4e0b\u6587..."
                    : hasItems
                        ? $"\u5df2\u8bc6\u522b {update.Snapshot.Items.Count} \u9879\u4e0a\u4e0b\u6587\uff0c\u64cd\u4f5c\u5c06\u81ea\u52a8\u5339\u914d"
                        : "\u5e38\u7528\u3001\u56fa\u5b9a\u4e0e\u6700\u8fd1\u64cd\u4f5c");
        }

        public static SuperPanelActionViewState ProjectAction(
            SuperPanelActionOutcome outcome)
        {
            if (outcome.Kind == SearchActionKind.ContinueSearch)
            {
                return new SuperPanelActionViewState(
                    SuperPanelActionDisposition.ContinueSearch,
                    string.Empty,
                    outcome.ContinuationQuery ?? string.Empty);
            }

            return new SuperPanelActionViewState(
                outcome.IsSuccess && !outcome.KeepPanelOpen
                    ? SuperPanelActionDisposition.Hide
                    : SuperPanelActionDisposition.Present,
                outcome.Message,
                null);
        }
    }

    internal sealed record SuperPanelContextViewState(
        IReadOnlyList<ContextItem>? Items,
        bool ShowBadges,
        string Summary);

    internal sealed record SuperPanelActionViewState(
        SuperPanelActionDisposition Disposition,
        string Status,
        string? ContinuationQuery);

    internal enum SuperPanelActionDisposition
    {
        Hide,
        Present,
        ContinueSearch,
    }
}
