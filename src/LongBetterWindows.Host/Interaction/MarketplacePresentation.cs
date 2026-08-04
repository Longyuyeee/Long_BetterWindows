using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal static class MarketplacePresentation
    {
        public static IReadOnlyList<MarketCardModel> ProjectEntries(
            MarketplaceCatalog catalog,
            string? query,
            string? category,
            Func<string, string?> getInstalledVersion,
            Func<string, string>? localizeCategory = null)
            => MarketplaceCatalogCodec.Search(catalog.Entries, query, category)
                .Select(entry =>
                {
                    var installedVersion = getInstalledVersion(entry.Id);
                    return new MarketCardModel(
                        entry,
                        LocalMarketplaceRepository.GetInstallState(
                            entry, installedVersion),
                        installedVersion,
                        localizeCategory?.Invoke(entry.Category) ?? entry.Category);
                })
                .ToArray();

        public static IReadOnlyList<string> GetCategories(MarketplaceCatalog catalog)
            => catalog.Entries
                .Select(entry => entry.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category)
                .ToArray();

        public static string GetCategoryResourceKey(string category)
            => category.ToLowerInvariant() switch
            {
                "automation" => "market.category.automation",
                "design" => "market.category.design",
                "developer" => "market.category.developer",
                "file" => "market.category.file",
                "productivity" => "market.category.productivity",
                "security" => "market.category.security",
                "system" => "market.category.system",
                "text" => "market.category.text",
                _ => string.Empty,
            };

        public static string LocalizeCategory(
            string category,
            Func<string, string> localize)
        {
            var resourceKey = GetCategoryResourceKey(category);
            return string.IsNullOrEmpty(resourceKey) ? category : localize(resourceKey);
        }

        public static MarketplaceCompatibility GetCompatibility(
            MarketplacePackageVersion version,
            string hostVersion)
        {
            var requirements = new List<string>();
            var compatible = true;
            if (!string.IsNullOrWhiteSpace(version.MinHostVersion))
            {
                requirements.Add($"Host >= {version.MinHostVersion}");
                compatible &= ParseVersion(hostVersion) >= ParseVersion(version.MinHostVersion);
            }
            if (!string.IsNullOrWhiteSpace(version.MinApiVersion))
            {
                requirements.Add($"API >= {version.MinApiVersion}");
                var requiredApi = ParseVersion(version.MinApiVersion);
                compatible &= ApiVersion.Current.IsCompatibleWith(new ApiVersion(
                    requiredApi.Major,
                    requiredApi.Minor,
                    Math.Max(0, requiredApi.Build)));
            }
            if (!string.IsNullOrWhiteSpace(version.MinUiKitVersion))
            {
                requirements.Add($"UI Kit >= {version.MinUiKitVersion}");
                compatible &= PluginPackageValidator.CurrentUiKitVersion
                    >= ParseVersion(version.MinUiKitVersion);
            }

            return new MarketplaceCompatibility(
                compatible,
                requirements);
        }

        public static MarketplacePackageMetadata CreatePackageMetadata(
            MarketplaceEntry entry,
            MarketplacePackageVersion version)
            => new()
            {
                Source = entry.Source,
                ExpectedPluginId = entry.Id,
                ExpectedVersion = version.Version,
                ExpectedSha256 = EmptyToNull(version.Sha256),
                Signature = version.Signature,
                PublisherPublicKeyPem = version.PublisherPublicKeyPem,
                PublisherKeyId = version.PublisherKeyId,
            };

        public static string GetInstallErrorResourceKey(InstallErrorCode code)
            => code switch
            {
                InstallErrorCode.SourceNotFound => "market.error.install.sourceNotFound",
                InstallErrorCode.InvalidPackageExtension => "market.error.install.invalidExtension",
                InstallErrorCode.PackageValidationFailed => "market.error.install.packageValidation",
                InstallErrorCode.InstallFailedRolledBack => "market.error.install.failedRolledBack",
                InstallErrorCode.InstallRollbackFailed => "market.error.install.rollbackFailed",
                InstallErrorCode.PluginNotInstalled => "market.error.install.notInstalled",
                InstallErrorCode.InstalledManifestInvalid => "market.error.install.manifestInvalid",
                InstallErrorCode.UninstallFailedRolledBack => "market.error.install.uninstallFailedRolledBack",
                InstallErrorCode.UninstallRollbackFailed => "market.error.install.uninstallRollbackFailed",
                _ => "market.error.unknown",
            };

        public static string GetErrorResourceKey(MarketplaceErrorCode code)
            => code switch
            {
                MarketplaceErrorCode.CatalogNotFound => "market.error.catalog.notFound",
                MarketplaceErrorCode.CatalogUnsupported => "market.error.catalog.unsupported",
                MarketplaceErrorCode.CatalogTooLarge => "market.error.catalog.tooLarge",
                MarketplaceErrorCode.CatalogDuplicatePlugin
                    or MarketplaceErrorCode.CatalogInvalidEntry => "market.error.catalog.invalid",
                MarketplaceErrorCode.CatalogUnreadable
                    or MarketplaceErrorCode.CatalogNetworkUnavailable
                    or MarketplaceErrorCode.CatalogAllSourcesUnavailable => "market.error.catalog.unavailable",
                MarketplaceErrorCode.CatalogInsecureRedirect => "market.error.catalog.insecure",
                MarketplaceErrorCode.DownloadNotConfigured => "market.error.download.notConfigured",
                MarketplaceErrorCode.DownloadUriNotAllowed
                    or MarketplaceErrorCode.DownloadRedirectNotAllowed => "market.error.download.sourceRejected",
                MarketplaceErrorCode.DownloadHashMissing => "market.error.download.metadataInvalid",
                MarketplaceErrorCode.DownloadCacheFailure => "market.error.download.cacheFailure",
                MarketplaceErrorCode.DownloadTooLarge => "market.error.download.tooLarge",
                MarketplaceErrorCode.DownloadHashMismatch => "market.error.download.hashMismatch",
                MarketplaceErrorCode.DownloadCanceled
                    or MarketplaceErrorCode.OperationCanceled => "market.status.canceled",
                MarketplaceErrorCode.DownloadTimeout => "market.error.download.timeout",
                MarketplaceErrorCode.DownloadFailed => "market.error.downloadFailed",
                MarketplaceErrorCode.PackageRejected => "market.error.packageRejected",
                MarketplaceErrorCode.OperationBusy => "market.error.busyWait",
                _ => "market.error.unknown",
            };

        public static string GetErrorAutomationStatus(MarketplaceErrorCode code)
            => code switch
            {
                MarketplaceErrorCode.DownloadTimeout => "NetworkTimeout",
                MarketplaceErrorCode.DownloadFailed
                    or MarketplaceErrorCode.CatalogNetworkUnavailable
                    or MarketplaceErrorCode.CatalogAllSourcesUnavailable => "NetworkUnavailable",
                MarketplaceErrorCode.DownloadHashMissing
                    or MarketplaceErrorCode.DownloadHashMismatch => "HashRejected",
                MarketplaceErrorCode.DownloadUriNotAllowed
                    or MarketplaceErrorCode.DownloadRedirectNotAllowed
                    or MarketplaceErrorCode.CatalogInsecureRedirect => "SourceRejected",
                MarketplaceErrorCode.CatalogNotFound
                    or MarketplaceErrorCode.CatalogUnsupported
                    or MarketplaceErrorCode.CatalogTooLarge
                    or MarketplaceErrorCode.CatalogDuplicatePlugin
                    or MarketplaceErrorCode.CatalogInvalidEntry
                    or MarketplaceErrorCode.CatalogUnreadable => "CatalogRejected",
                MarketplaceErrorCode.OperationBusy => "Busy",
                MarketplaceErrorCode.DownloadCanceled
                    or MarketplaceErrorCode.OperationCanceled => "Canceled",
                _ => "Rejected",
            };

        public static Version ParseVersion(string? value)
        {
            var normalized = (value ?? "0.0.0").TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(normalized, out var version)
                ? version
                : new Version(0, 0, 0);
        }

        private static string? EmptyToNull(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal sealed record MarketplaceCompatibility(
        bool IsCompatible,
        IReadOnlyList<string> Requirements);

    internal sealed class MarketCardModel
    {
        public MarketCardModel(
            MarketplaceEntry entry,
            MarketplaceInstallState state,
            string? installedVersion,
            string? categoryLabel = null)
        {
            Entry = entry;
            State = state;
            InstalledVersion = installedVersion;
            CategoryLabel = string.IsNullOrWhiteSpace(categoryLabel)
                ? entry.Category
                : categoryLabel;
        }

        public MarketplaceEntry Entry { get; }
        public MarketplaceInstallState State { get; }
        public string? InstalledVersion { get; }
        public string CategoryLabel { get; }
        public string Name => Entry.Name;
        public string Summary => Entry.Summary;
        public string Monogram => string.IsNullOrWhiteSpace(Name)
            ? "L"
            : Name[..1].ToUpperInvariant();
        public string Meta => $"{CategoryLabel} · {Entry.Publisher}";
    }

    internal sealed record MarketplaceCategoryOption(string? Value, string Label)
    {
        public override string ToString() => Label;
    }
}
