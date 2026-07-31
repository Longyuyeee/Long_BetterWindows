using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceBundleVerificationPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<MarketplaceBundleVerificationReport> VerifyAsync(
        MarketplaceBundleVerificationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = Path.GetFullPath(options.BundleDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Marketplace bundle was not found.");
        if (string.IsNullOrWhiteSpace(options.ExpectedPublisherKeyId)
            || options.ExpectedPublicKeyFingerprint.Length != 64
            || !options.ExpectedPublicKeyFingerprint.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected publisher Key ID and 64-character SHA-256 fingerprint are required.");

        _ = await MarketplaceDeploymentPipeline.CreatePlanAsync(root, cancellationToken);
        var catalogResult = await new LocalMarketplaceRepository(Path.Combine(root, "registry.json"))
            .LoadAsync(cancellationToken);
        if (!catalogResult.IsSuccess || catalogResult.Catalog == null)
            throw new InvalidDataException(catalogResult.Error ?? "Marketplace Registry is invalid.");

        var trust = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(
            Path.Combine(root, "trusted-publisher.fragment.json"), cancellationToken);
        if (!trust.IsSuccess || trust.KeyCount != 1)
            throw new InvalidDataException(trust.Error ?? "Reference bundle requires exactly one trusted publisher key.");

        var publishReport = await ReadAsync<MarketplacePublishReport>(
            Path.Combine(root, "publish-report.json"), cancellationToken)
            ?? throw new InvalidDataException("Publish report is invalid.");
        if (!string.Equals(publishReport.PublisherKeyId, options.ExpectedPublisherKeyId,
                StringComparison.Ordinal)
            || !string.Equals(publishReport.PublicKeyFingerprint,
                options.ExpectedPublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bundle publisher identity does not match the expected trusted root.");
        var validator = new PluginPackageValidator(trustStore: trust.Store);
        var verified = new List<MarketplaceBundleVerifiedPackage>();
        foreach (var entry in catalogResult.Catalog.Entries)
        foreach (var version in entry.Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (version.PackageUri == null || !version.PackageUri.IsAbsoluteUri
                || version.PackageUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Bundle package URI is invalid: {entry.Id} {version.Version}");
            var packageName = Uri.UnescapeDataString(Path.GetFileName(version.PackageUri.AbsolutePath));
            var packagePath = ResolveWithin(Path.Combine(root, "packages"), packageName);
            var validation = await validator.ValidateAsync(packagePath, new MarketplacePackageMetadata
            {
                Source = MarketplaceSourceKind.RemoteRegistry,
                ExpectedPluginId = entry.Id,
                ExpectedVersion = version.Version,
                ExpectedSha256 = version.Sha256,
                Signature = version.Signature,
                PublisherKeyId = version.PublisherKeyId,
                PublisherPublicKeyPem = version.PublisherPublicKeyPem,
            });
            if (!validation.IsSuccess || validation.TrustLevel != PackageTrustLevel.PublisherSigned)
                throw new InvalidDataException(
                    $"Bundle signature verification failed for {entry.Id} {version.Version}: {validation.Error}");
            verified.Add(new MarketplaceBundleVerifiedPackage
            {
                PluginId = entry.Id,
                Version = version.Version,
                File = $"packages/{packageName}",
                Sha256 = validation.Sha256 ?? version.Sha256,
                Bytes = new FileInfo(packagePath).Length,
            });
        }
        if (verified.Count == 0 || verified.Count != publishReport.Packages.Count)
            throw new InvalidDataException("Verified package count does not match the publish report.");

        var report = new MarketplaceBundleVerificationReport
        {
            SchemaVersion = 1,
            VerifiedAt = DateTimeOffset.UtcNow,
            BundleDirectory = root,
            Publisher = publishReport.Publisher,
            PublisherKeyId = publishReport.PublisherKeyId,
            PublicKeyFingerprint = publishReport.PublicKeyFingerprint,
            PackageCount = verified.Count,
            Packages = verified,
        };
        if (!string.IsNullOrWhiteSpace(options.ReportPath))
            await WriteAtomicallyAsync(Path.GetFullPath(options.ReportPath), report, cancellationToken);
        return report;
    }

    private static string ResolveWithin(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Bundle package path must be relative.");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException("Bundle package path is missing or escapes the package directory.");
        return fullPath;
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteAtomicallyAsync(
        string path, MarketplaceBundleVerificationReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}

public sealed class MarketplaceBundleVerificationOptions
{
    public required string BundleDirectory { get; init; }
    public required string ExpectedPublisherKeyId { get; init; }
    public required string ExpectedPublicKeyFingerprint { get; init; }
    public string? ReportPath { get; init; }
}

public sealed class MarketplaceBundleVerificationReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset VerifiedAt { get; init; }
    public string BundleDirectory { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string PublisherKeyId { get; init; } = string.Empty;
    public string PublicKeyFingerprint { get; init; } = string.Empty;
    public int PackageCount { get; init; }
    public IReadOnlyList<MarketplaceBundleVerifiedPackage> Packages { get; init; }
        = Array.Empty<MarketplaceBundleVerifiedPackage>();
}

public sealed class MarketplaceBundleVerifiedPackage
{
    public string PluginId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
}

public static class BundleVerificationArguments
{
    public static MarketplaceBundleVerificationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ArgumentException($"Unexpected or incomplete argument: {args[i]}");
            values[args[i][2..]] = args[++i];
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required argument --{key}");
        return new MarketplaceBundleVerificationOptions
        {
            BundleDirectory = Required("bundle"),
            ExpectedPublisherKeyId = Required("key-id"),
            ExpectedPublicKeyFingerprint = Required("fingerprint"),
            ReportPath = values.GetValueOrDefault("report"),
        };
    }
}
