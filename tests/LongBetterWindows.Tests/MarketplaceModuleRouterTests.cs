using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class MarketplaceModuleRouterTests
{
    [Fact]
    public void DetailAndBack_PreserveLastSelectionInsideCatalogModule()
    {
        var router = new MarketplaceModuleRouter();

        router.OpenDetail("COM.LONG.BASE64");

        Assert.Equal("detail:com.long.base64", router.Route.CanonicalValue);
        Assert.True(router.BackToCatalog());
        Assert.Equal("catalog", router.Route.CanonicalValue);
        Assert.Equal("com.long.base64", router.LastSelectedPluginId);
    }

    [Fact]
    public void Reconcile_RemovesUnavailableDetailRoute()
    {
        var router = new MarketplaceModuleRouter();
        router.OpenDetail("com.long.base64");

        Assert.True(router.Reconcile(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "com.long.other",
            }));

        Assert.Equal(MarketplaceModuleRouteKind.Catalog, router.Route.Kind);
        Assert.Null(router.LastSelectedPluginId);
        Assert.False(router.BackToCatalog());
    }

    [Theory]
    [InlineData(true, (int)MarketplaceModuleRouteKind.Detail, false, true)]
    [InlineData(false, (int)MarketplaceModuleRouteKind.Detail, false, false)]
    [InlineData(true, (int)MarketplaceModuleRouteKind.Catalog, false, false)]
    [InlineData(true, (int)MarketplaceModuleRouteKind.Detail, true, false)]
    public void NavigationPolicy_OnlyTreatsCompactDetailAsBackStack(
        bool compact,
        int route,
        bool confirmation,
        bool expected)
    {
        Assert.Equal(
            expected,
            MarketplaceModuleNavigationPolicy.CanNavigateBack(
                compact,
                (MarketplaceModuleRouteKind)route,
                confirmation));
    }
}
