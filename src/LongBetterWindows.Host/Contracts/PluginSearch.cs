namespace LongBetterWindows.Host.Contracts;

public interface IPluginSearchProvider
{
    int Priority { get; }

    Task<IReadOnlyList<PluginSearchResult>> SearchAsync(
        PluginSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record PluginSearchRequest(
    string Query,
    int MaxResults = 20,
    IReadOnlyList<string>? PinnedResultIds = null,
    IReadOnlyList<string>? RecentResultIds = null,
    IReadOnlyList<string>? AdditionalPreferredResultIds = null);

public enum PluginSearchResultKind
{
    Data,
    Continuation,
}

public enum PluginSearchActionKind
{
    ExecuteCommand,
    ContinueSearch,
}

public sealed record PluginSearchAction(
    PluginSearchActionKind Kind,
    string Target,
    PluginCommandInvocation? Invocation = null,
    string Label = "");

public sealed record PluginSearchResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public int Score { get; init; }
    public PluginSearchResultKind Kind { get; init; } =
        PluginSearchResultKind.Data;
    public required PluginSearchAction PrimaryAction { get; init; }
    public IReadOnlyList<PluginSearchAction> SecondaryActions { get; init; } =
        Array.Empty<PluginSearchAction>();
    public bool CanPin { get; init; }
    public string? ContinuationToken { get; init; }
}
