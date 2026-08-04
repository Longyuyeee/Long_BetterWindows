using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

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

    internal enum MarketplaceRecoveryStrategy
    {
        RemovePartialInstall,
        RestoreInstalledVersion,
    }

    internal sealed record MarketplaceInstallReview(
        MarketplaceOperationPresentation Operation,
        string PluginName,
        string TargetVersion,
        string? InstalledVersion,
        string? DeclaredPublisher,
        string? PublisherKeyId,
        PackageTrustLevel TrustLevel,
        string Sha256,
        MarketplaceCompatibility Compatibility,
        PermissionDiff PermissionDiff,
        bool RequiresHighTrustWarning,
        MarketplaceRecoveryStrategy RecoveryStrategy);

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

        public static MarketplaceInstallReview CreateInstallReview(
            PackageValidationResult validation,
            PluginManifest? installedManifest,
            MarketplacePackageMetadata? metadata,
            string hostVersion)
        {
            if (!validation.IsSuccess || validation.Manifest == null)
                throw new ArgumentException(
                    "A successful package validation is required.",
                    nameof(validation));

            var manifest = validation.Manifest;
            var version = new MarketplacePackageVersion
            {
                Version = manifest.Version,
                MinHostVersion = manifest.MinHostVersion,
                MinApiVersion = manifest.MinApiVersion,
                MinUiKitVersion = manifest.MinUiKitVersion,
            };
            return new MarketplaceInstallReview(
                ForInstall(installedManifest?.Version, manifest.Version),
                manifest.Name,
                manifest.Version,
                installedManifest?.Version,
                string.IsNullOrWhiteSpace(manifest.Author) ? null : manifest.Author,
                validation.TrustLevel == PackageTrustLevel.PublisherSigned
                    ? metadata?.PublisherKeyId
                    : null,
                validation.TrustLevel,
                validation.Sha256 ?? string.Empty,
                MarketplacePresentation.GetCompatibility(version, hostVersion),
                validation.PermissionDiff,
                validation.RequiresHighTrustWarning,
                installedManifest == null
                    ? MarketplaceRecoveryStrategy.RemovePartialInstall
                    : MarketplaceRecoveryStrategy.RestoreInstalledVersion);
        }

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
