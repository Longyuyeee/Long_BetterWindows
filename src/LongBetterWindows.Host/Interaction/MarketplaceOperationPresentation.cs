namespace LongBetterWindows.Host.Interaction
{
    internal enum MarketplaceOperationIntent
    {
        Install,
        Update,
        Downgrade,
        Reinstall,
        Uninstall,
    }

    internal sealed record MarketplaceOperationPresentation(
        MarketplaceOperationIntent Intent,
        string ReviewTitleResourceKey,
        string ConfirmActionResourceKey,
        string ProgressResourceKey,
        string SuccessResourceKey,
        string RemoteActionResourceKey,
        string LocalActionResourceKey);

    internal static class MarketplaceOperationPresenter
    {
        public static MarketplaceOperationPresentation ForInstall(
            string? installedVersion,
            string targetVersion)
        {
            if (string.IsNullOrWhiteSpace(installedVersion))
                return Create(MarketplaceOperationIntent.Install);

            var installed = MarketplacePresentation.ParseVersion(installedVersion);
            var target = MarketplacePresentation.ParseVersion(targetVersion);
            return Create(target > installed
                ? MarketplaceOperationIntent.Update
                : target < installed
                    ? MarketplaceOperationIntent.Downgrade
                    : MarketplaceOperationIntent.Reinstall);
        }

        public static MarketplaceOperationPresentation ForUninstall()
            => Create(MarketplaceOperationIntent.Uninstall);

        private static MarketplaceOperationPresentation Create(
            MarketplaceOperationIntent intent)
        {
            var suffix = intent.ToString().ToLowerInvariant();
            return new MarketplaceOperationPresentation(
                intent,
                $"market.operation.{suffix}.review",
                $"market.operation.{suffix}.confirm",
                $"market.operation.{suffix}.progress",
                $"market.operation.{suffix}.success",
                $"market.operation.{suffix}.remoteAction",
                $"market.operation.{suffix}.localAction");
        }
    }
}
