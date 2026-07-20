namespace LongBetterWindows.Host.Contracts
{
    public enum MarketplaceSourceKind
    {
        LocalPackage,
        RemoteRegistry,
    }

    public enum PackageTrustLevel
    {
        Untrusted,
        LocalUnsigned,
        PublisherSigned,
    }

    public enum MarketplaceInstallState
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
        DowngradeAvailable,
        Incompatible,
    }

    public sealed class MarketplaceEntry
    {
        public MarketplaceSourceKind Source { get; init; } = MarketplaceSourceKind.LocalPackage;
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Publisher { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<MarketplacePackageVersion> Versions { get; init; }
            = Array.Empty<MarketplacePackageVersion>();
    }

    public sealed class MarketplaceCatalog
    {
        public int SchemaVersion { get; init; } = 1;
        public MarketplaceSourceKind Source { get; init; } = MarketplaceSourceKind.LocalPackage;
        public DateTimeOffset GeneratedAt { get; init; }
        public IReadOnlyList<MarketplaceEntry> Entries { get; init; } = Array.Empty<MarketplaceEntry>();
    }

    public sealed class MarketplacePackageVersion
    {
        public string Version { get; init; } = string.Empty;
        public Uri? PackageUri { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public string? Signature { get; init; }
        public string? PublisherPublicKeyPem { get; init; }
        public string? PublisherKeyId { get; init; }
        public DateTimeOffset PublishedAt { get; init; }
        public string ReleaseNotes { get; init; } = string.Empty;
        public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
        public string? MinHostVersion { get; init; }
        public string? MinApiVersion { get; init; }
        public string? MinUiKitVersion { get; init; }

        public override string ToString() => Version;
    }

    public sealed class MarketplacePackageMetadata
    {
        public MarketplaceSourceKind Source { get; init; } = MarketplaceSourceKind.LocalPackage;
        public string? ExpectedPluginId { get; init; }
        public string? ExpectedVersion { get; init; }
        public string? ExpectedSha256 { get; init; }
        public string? Signature { get; init; }
        public string? PublisherPublicKeyPem { get; init; }
        public string? PublisherKeyId { get; init; }
    }

    public sealed class MarketplaceSettings
    {
        public int SchemaVersion { get; init; } = 1;
        public Uri? RegistryUri { get; init; }
        public int CatalogTimeoutSeconds { get; init; } = 8;
        public int DownloadTimeoutSeconds { get; init; } = 60;
        public IReadOnlyList<string> AllowedPackageHosts { get; init; } = Array.Empty<string>();
    }

    public sealed class TrustedPublisherDocument
    {
        public int SchemaVersion { get; init; } = 1;
        public IReadOnlyList<TrustedPublisherKey> Publishers { get; init; }
            = Array.Empty<TrustedPublisherKey>();
    }

    public sealed class TrustedPublisherKey
    {
        public string KeyId { get; init; } = string.Empty;
        public string Publisher { get; init; } = string.Empty;
        public string Algorithm { get; init; } = "RSA-SHA256";
        public string PublicKeyPem { get; init; } = string.Empty;
        public string Sha256Fingerprint { get; init; } = string.Empty;
    }

    public sealed class PermissionDiff
    {
        public IReadOnlyList<string> Added { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Removed { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Unchanged { get; init; } = Array.Empty<string>();
        public bool HasElevatedChanges => Added.Count > 0;
    }
}
