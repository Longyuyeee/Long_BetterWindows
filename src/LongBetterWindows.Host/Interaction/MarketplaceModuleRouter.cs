namespace LongBetterWindows.Host.Interaction
{
    internal enum MarketplaceModuleRouteKind
    {
        Catalog,
        Detail,
    }

    internal readonly record struct MarketplaceModuleRoute(
        MarketplaceModuleRouteKind Kind,
        string? PluginId = null)
    {
        public string CanonicalValue => Kind == MarketplaceModuleRouteKind.Catalog
            ? "catalog"
            : $"detail:{PluginId}";
    }

    internal sealed class MarketplaceModuleRouter
    {
        public MarketplaceModuleRoute Route { get; private set; } =
            new(MarketplaceModuleRouteKind.Catalog);

        public string? LastSelectedPluginId { get; private set; }

        public void OpenDetail(string pluginId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            var normalized = pluginId.Trim().ToLowerInvariant();
            LastSelectedPluginId = normalized;
            Route = new MarketplaceModuleRoute(
                MarketplaceModuleRouteKind.Detail,
                normalized);
        }

        public bool BackToCatalog()
        {
            if (Route.Kind != MarketplaceModuleRouteKind.Detail)
                return false;
            Route = new MarketplaceModuleRoute(MarketplaceModuleRouteKind.Catalog);
            return true;
        }

        public bool Reconcile(IReadOnlySet<string> availablePluginIds)
        {
            ArgumentNullException.ThrowIfNull(availablePluginIds);
            if (Route.Kind != MarketplaceModuleRouteKind.Detail
                || Route.PluginId is null
                || availablePluginIds.Contains(Route.PluginId))
            {
                return false;
            }

            LastSelectedPluginId = null;
            Route = new MarketplaceModuleRoute(MarketplaceModuleRouteKind.Catalog);
            return true;
        }

        public void Reset()
        {
            LastSelectedPluginId = null;
            Route = new MarketplaceModuleRoute(MarketplaceModuleRouteKind.Catalog);
        }
    }

    internal static class MarketplaceModuleNavigationPolicy
    {
        public static bool CanNavigateBack(
            bool isCompactLayout,
            MarketplaceModuleRouteKind routeKind,
            bool hasConfirmation)
            => isCompactLayout
                && routeKind == MarketplaceModuleRouteKind.Detail
                && !hasConfirmation;
    }
}
