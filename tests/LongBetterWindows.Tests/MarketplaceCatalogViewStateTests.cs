using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceCatalogViewStateTests
{
    [Fact]
    public void FailedLoad_IsBlockingAndRetryable()
    {
        var state = MarketplaceCatalogViewStatePresenter.FromLoad(
            MarketplaceCatalogResult.Fail(
                MarketplaceErrorCode.CatalogNetworkUnavailable,
                "network unavailable"));

        Assert.Equal(MarketplaceCatalogViewStateKind.Error, state.Kind);
        Assert.True(state.ShowsBlockingState);
        Assert.True(state.CanRetry);
        Assert.False(state.ShowsCatalog);
        Assert.Equal(
            "market.error.catalog.unavailable",
            state.DescriptionResourceKey);
    }

    [Fact]
    public void FallbackLoad_KeepsTrustedCatalogVisibleWithNotice()
    {
        var state = MarketplaceCatalogViewStatePresenter.FromLoad(
            MarketplaceCatalogResult.Ok(Catalog(), fallback: true));

        Assert.Equal(MarketplaceCatalogViewStateKind.Degraded, state.Kind);
        Assert.True(state.ShowsCatalog);
        Assert.True(state.ShowsNotice);
        Assert.True(state.CanRetry);
        Assert.False(state.ShowsBlockingState);
    }

    [Fact]
    public void EmptyFilter_DiffersFromEmptyCatalogAndDoesNotOfferNetworkRetry()
    {
        var state = MarketplaceCatalogViewStatePresenter.FromFilter(
            resultCount: 0,
            hasActiveFilter: true,
            isDegraded: false);

        Assert.Equal(MarketplaceCatalogViewStateKind.EmptyFilter, state.Kind);
        Assert.Equal("market.catalog.noResults.desc", state.DescriptionResourceKey);
        Assert.False(state.CanRetry);
    }

    [Fact]
    public void EmptyCatalog_IsRetryable()
    {
        var state = MarketplaceCatalogViewStatePresenter.FromLoad(
            MarketplaceCatalogResult.Ok(new MarketplaceCatalog()));

        Assert.Equal(MarketplaceCatalogViewStateKind.EmptyCatalog, state.Kind);
        Assert.True(state.ShowsBlockingState);
        Assert.True(state.CanRetry);
    }

    private static MarketplaceCatalog Catalog()
        => new()
        {
            Entries =
            [
                new MarketplaceEntry
                {
                    Id = "dev.long.fixture",
                    Name = "Fixture",
                    Summary = "Fixture plugin",
                    Publisher = "Long",
                    Category = "Quality",
                    Versions = [new MarketplacePackageVersion { Version = "1.0.0" }],
                },
            ],
        };
}
