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
            Func<string, string?> getInstalledVersion)
            => MarketplaceCatalogCodec.Search(catalog.Entries, query, category)
                .Select(entry =>
                {
                    var installedVersion = getInstalledVersion(entry.Id);
                    return new MarketCardModel(
                        entry,
                        LocalMarketplaceRepository.GetInstallState(
                            entry, installedVersion),
                        installedVersion);
                })
                .ToArray();

        public static IReadOnlyList<string> GetCategories(MarketplaceCatalog catalog)
            => catalog.Entries
                .Select(entry => entry.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category)
                .ToArray();

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
            string? installedVersion)
        {
            Entry = entry;
            State = state;
            InstalledVersion = installedVersion;
        }

        public MarketplaceEntry Entry { get; }
        public MarketplaceInstallState State { get; }
        public string? InstalledVersion { get; }
        public string Name => Entry.Name;
        public string Summary => Entry.Summary;
        public string Monogram => string.IsNullOrWhiteSpace(Name)
            ? "L"
            : Name[..1].ToUpperInvariant();
        public string Meta => $"{Entry.Category} · {Entry.Publisher}";
    }
}
