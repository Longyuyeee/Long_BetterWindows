using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public sealed class PluginDistributionEligibility
    {
        public bool LocalImportEligible { get; init; }
        public bool RemoteMarketplacePackageEligible { get; init; }
        public bool RemoteMarketplaceCurrentlyTrusted { get; init; }
        public bool RemoteMarketplaceRequiresPublisherSignature { get; init; }
        public string? RemoteMarketplaceBlockReason { get; init; }
    }

    public static class PluginDistributionPolicy
    {
        public const string ValidationFailed = "validation_failed";
        public const string HighTrustRuntimeNotSupported =
            "high_trust_runtime_not_supported";

        public static PluginDistributionEligibility Assess(
            PackageValidationResult validation)
        {
            ArgumentNullException.ThrowIfNull(validation);

            var valid = validation.IsSuccess && validation.Manifest is not null;
            var remotePackageEligible =
                valid && !validation.RequiresHighTrustWarning;
            var remotelyTrusted =
                remotePackageEligible
                && validation.TrustLevel == PackageTrustLevel.PublisherSigned;

            return new PluginDistributionEligibility
            {
                LocalImportEligible = valid,
                RemoteMarketplacePackageEligible = remotePackageEligible,
                RemoteMarketplaceCurrentlyTrusted = remotelyTrusted,
                RemoteMarketplaceRequiresPublisherSignature =
                    remotePackageEligible && !remotelyTrusted,
                RemoteMarketplaceBlockReason = !valid
                    ? ValidationFailed
                    : validation.RequiresHighTrustWarning
                        ? HighTrustRuntimeNotSupported
                        : null,
            };
        }
    }
}
