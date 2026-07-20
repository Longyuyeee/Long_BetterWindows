namespace LongBetterWindows.Host.Interaction
{
    internal static class SearchResultActionMenuProjection
    {
        public static IReadOnlyList<SearchResultActionMenuItem> Build(
            SearchResultItem result) => result.SecondaryActions
            .Select((action, index) => new SearchResultActionMenuItem(
                string.IsNullOrWhiteSpace(action.Label) ? "执行" : action.Label,
                $"Long.Result.SecondaryAction.{index}",
                action))
            .ToList();
    }

    internal sealed record SearchResultActionMenuItem(
        string Header,
        string AutomationId,
        SearchResultAction Action);
}
