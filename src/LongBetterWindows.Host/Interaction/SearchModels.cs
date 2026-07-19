namespace LongBetterWindows.Host.Interaction
{
    public enum SearchResultKind
    {
        Command,
        Data,
        Continuation,
    }

    public enum SearchActionKind
    {
        ExecuteCommand,
        ContinueSearch,
        OpenPath,
        OpenContainingFolder,
        OpenUri,
        CopyText,
    }

    public sealed record SearchRequest(
        string Query,
        ContextSnapshot Context,
        int MaxResults = 20,
        IReadOnlyList<string>? PinnedResultIds = null,
        IReadOnlyList<string>? RecentResultIds = null,
        IReadOnlyList<string>? AdditionalPreferredResultIds = null);

    public sealed record SearchRunMetrics(
        TimeSpan? FirstBatchElapsed,
        TimeSpan TotalElapsed,
        int ProviderCount,
        int BatchCount,
        int ResultCount);

    public sealed record SearchResultAction(
        SearchActionKind Kind,
        string Target,
        Contracts.PluginCommandInvocation? Invocation = null,
        string Label = "");

    public sealed record SearchResultItem
    {
        public required string Id { get; init; }
        public required string ProviderId { get; init; }
        public required string Title { get; init; }
        public string Subtitle { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public int Score { get; init; }
        public int ProviderPriority { get; init; }
        public int PreferenceScore { get; init; }
        public SearchResultKind Kind { get; init; } = SearchResultKind.Data;
        public required SearchResultAction PrimaryAction { get; init; }
        public IReadOnlyList<SearchResultAction> SecondaryActions { get; init; } =
            Array.Empty<SearchResultAction>();
        public bool HasSecondaryActions => SecondaryActions.Count > 0;
        public bool CanPin { get; init; }
        public bool IsPinned { get; init; }
        public string? ContinuationToken { get; init; }
    }
}
