using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class StableSelectionResolverTests
{
    [Fact]
    public void Resolve_PreservesStableIdWhenHigherRankedBatchArrives()
    {
        var results = new[]
        {
            Result("new-high"),
            Result("first"),
            Result("selected"),
        };

        var resolution = StableSelectionResolver.Resolve(
            results,
            "selected",
            item => item.Id);

        Assert.Equal(2, resolution.Index);
        Assert.True(resolution.Preserved);
    }

    [Fact]
    public void Resolve_FallsBackToFirstItemWhenSelectionDisappears()
    {
        var resolution = StableSelectionResolver.Resolve(
            new[] { Result("first"), Result("second") },
            "removed",
            item => item.Id);

        Assert.Equal(0, resolution.Index);
        Assert.False(resolution.Preserved);
    }

    [Fact]
    public void Resolve_ReturnsNoSelectionForEmptyResults()
    {
        var resolution = StableSelectionResolver.Resolve(
            Array.Empty<SearchResultItem>(),
            "selected",
            item => item.Id);

        Assert.Equal(-1, resolution.Index);
        Assert.False(resolution.Preserved);
    }

    private static SearchResultItem Result(string id) => new()
    {
        Id = id,
        ProviderId = "test",
        Title = id,
        PrimaryAction = new SearchResultAction(
            SearchActionKind.ContinueSearch,
            id),
    };
}
