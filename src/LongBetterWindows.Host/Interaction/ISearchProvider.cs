namespace LongBetterWindows.Host.Interaction
{
    public interface ISearchProvider
    {
        string Id { get; }
        int Priority { get; }

        Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default);
    }
}
