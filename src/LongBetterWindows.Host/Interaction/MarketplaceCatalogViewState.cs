using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal enum MarketplaceCatalogViewStateKind
    {
        Loading,
        Ready,
        Degraded,
        EmptyCatalog,
        EmptyFilter,
        Error,
    }

    internal sealed record MarketplaceCatalogViewState(
        MarketplaceCatalogViewStateKind Kind,
        string TitleResourceKey,
        string DescriptionResourceKey,
        bool ShowsCatalog,
        bool ShowsProgress,
        bool CanRetry)
    {
        public bool ShowsBlockingState => Kind is
            MarketplaceCatalogViewStateKind.Loading or
            MarketplaceCatalogViewStateKind.EmptyCatalog or
            MarketplaceCatalogViewStateKind.EmptyFilter or
            MarketplaceCatalogViewStateKind.Error;

        public bool ShowsNotice => Kind == MarketplaceCatalogViewStateKind.Degraded;
    }

    internal static class MarketplaceCatalogViewStatePresenter
    {
        public static MarketplaceCatalogViewState Loading()
            => State(
                MarketplaceCatalogViewStateKind.Loading,
                "market.catalog.loading.title",
                "market.catalog.loading.desc",
                showsProgress: true);

        public static MarketplaceCatalogViewState FromLoad(MarketplaceCatalogResult result)
        {
            if (!result.IsSuccess)
            {
                return State(
                    MarketplaceCatalogViewStateKind.Error,
                    "market.catalog.error.title",
                    MarketplacePresentation.GetErrorResourceKey(result.ErrorCode),
                    canRetry: true);
            }

            if (result.Catalog?.Entries.Count == 0)
            {
                return State(
                    MarketplaceCatalogViewStateKind.EmptyCatalog,
                    "market.catalog.empty.title",
                    "market.catalog.empty.desc",
                    canRetry: true);
            }

            return result.IsFallback
                ? State(
                    MarketplaceCatalogViewStateKind.Degraded,
                    "market.catalog.degraded.title",
                    "market.catalog.degraded.desc",
                    showsCatalog: true,
                    canRetry: true)
                : State(
                    MarketplaceCatalogViewStateKind.Ready,
                    string.Empty,
                    string.Empty,
                    showsCatalog: true);
        }

        public static MarketplaceCatalogViewState FromFilter(
            int resultCount,
            bool hasActiveFilter,
            bool isDegraded)
        {
            if (resultCount == 0)
            {
                return State(
                    hasActiveFilter
                        ? MarketplaceCatalogViewStateKind.EmptyFilter
                        : MarketplaceCatalogViewStateKind.EmptyCatalog,
                    hasActiveFilter
                        ? "market.catalog.noResults.title"
                        : "market.catalog.empty.title",
                    hasActiveFilter
                        ? "market.catalog.noResults.desc"
                        : "market.catalog.empty.desc",
                    canRetry: !hasActiveFilter);
            }

            return isDegraded
                ? State(
                    MarketplaceCatalogViewStateKind.Degraded,
                    "market.catalog.degraded.title",
                    "market.catalog.degraded.desc",
                    showsCatalog: true,
                    canRetry: true)
                : State(
                    MarketplaceCatalogViewStateKind.Ready,
                    string.Empty,
                    string.Empty,
                    showsCatalog: true);
        }

        private static MarketplaceCatalogViewState State(
            MarketplaceCatalogViewStateKind kind,
            string title,
            string description,
            bool showsCatalog = false,
            bool showsProgress = false,
            bool canRetry = false)
            => new(kind, title, description, showsCatalog, showsProgress, canRetry);
    }
}
