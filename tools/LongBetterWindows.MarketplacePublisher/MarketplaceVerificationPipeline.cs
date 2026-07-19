using System.Diagnostics;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceVerificationPipeline
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task<MarketplaceVerificationReport> VerifyAsync(
        MarketplaceVerificationOptions options,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var registryUri = ValidateRegistryUri(options.RegistryUri);
        var trustPath = Path.GetFullPath(Required(options.TrustStorePath, nameof(options.TrustStorePath)));
        var trust = await MarketplaceConfigurationLoader.LoadTrustStoreAsync(trustPath, cancellationToken);
        if (!trust.IsSuccess || trust.KeyCount == 0)
            throw new InvalidDataException(trust.Error ?? "Publisher trust store is empty.");

        var allowedHosts = new HashSet<string>(
            options.AllowedPackageHosts.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase)
        {
            registryUri.IdnHost,
        };
        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 2, 300));
        var ownsClient = client == null;
        client ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = timeout,
        };

        var workDirectory = Path.Combine(Path.GetTempPath(), $"long-market-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var repository = new RemoteMarketplaceRepository(
                client, registryUri, Path.Combine(workDirectory, "registry-cache.json"), timeout);
            var catalogResult = await repository.LoadAsync(cancellationToken);
            if (!catalogResult.IsSuccess || catalogResult.Catalog == null || catalogResult.IsFallback)
                throw new InvalidDataException(catalogResult.Error ?? "Public Registry could not be loaded.");

            var catalog = catalogResult.Catalog
                ?? throw new InvalidDataException("Public Registry could not be loaded.");
            var versions = catalog.Entries.SelectMany(entry =>
                entry.Versions.Select(version => (Entry: entry, Version: version))).ToArray();
            if (versions.Length == 0)
                throw new InvalidDataException("Public Registry contains no package versions.");

            var downloader = new MarketplacePackageDownloader(
                client, Path.Combine(workDirectory, "packages"), allowedHosts, timeout);
            var validator = new PluginPackageValidator(trustStore: trust.Store);
            long totalBytes = 0;
            var verified = new List<MarketplaceVerifiedPackage>(versions.Length);
            foreach (var item in versions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var download = await downloader.DownloadAsync(item.Entry.Id, item.Version, cancellationToken);
                if (!download.IsSuccess || string.IsNullOrWhiteSpace(download.PackagePath))
                    throw new InvalidDataException(
                        $"Package download verification failed for {item.Entry.Id} {item.Version.Version}: {download.Error}");

                var validation = await validator.ValidateAsync(download.PackagePath, new MarketplacePackageMetadata
                {
                    Source = MarketplaceSourceKind.RemoteRegistry,
                    ExpectedPluginId = item.Entry.Id,
                    ExpectedVersion = item.Version.Version,
                    ExpectedSha256 = item.Version.Sha256,
                    Signature = item.Version.Signature,
                    PublisherKeyId = item.Version.PublisherKeyId,
                    PublisherPublicKeyPem = item.Version.PublisherPublicKeyPem,
                });
                if (!validation.IsSuccess || validation.TrustLevel != PackageTrustLevel.PublisherSigned)
                    throw new InvalidDataException(
                        $"Package trust verification failed for {item.Entry.Id} {item.Version.Version}: {validation.Error}");

                totalBytes += download.Bytes;
                verified.Add(new MarketplaceVerifiedPackage
                {
                    PluginId = item.Entry.Id,
                    Version = item.Version.Version,
                    Sha256 = validation.Sha256 ?? item.Version.Sha256,
                    PublisherKeyId = item.Version.PublisherKeyId ?? string.Empty,
                    Bytes = download.Bytes,
                });
            }

            stopwatch.Stop();
            var report = new MarketplaceVerificationReport
            {
                SchemaVersion = 1,
                VerifiedAt = DateTimeOffset.UtcNow,
                RegistryUri = registryUri,
                RegistryGeneratedAt = catalog.GeneratedAt,
                EntryCount = catalog.Entries.Count,
                PackageCount = verified.Count,
                TotalPackageBytes = totalBytes,
                TrustedPublisherKeyCount = trust.KeyCount,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                Packages = verified,
            };
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
                await WriteReportAtomicallyAsync(Path.GetFullPath(options.ReportPath), report, cancellationToken);
            return report;
        }
        finally
        {
            if (ownsClient) client.Dispose();
            TryDelete(workDirectory);
        }
    }

    private static Uri ValidateRegistryUri(Uri? uri)
    {
        if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Registry URI must be an absolute HTTPS URI without credentials or fragments.");
        return uri;
    }

    private static async Task WriteReportAtomicallyAsync(
        string path, MarketplaceVerificationReport report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary, JsonSerializer.Serialize(report, ReportJsonOptions), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value;
    private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

public sealed class MarketplaceVerificationOptions
{
    public required Uri RegistryUri { get; init; }
    public required string TrustStorePath { get; init; }
    public IReadOnlyList<string> AllowedPackageHosts { get; init; } = Array.Empty<string>();
    public int TimeoutSeconds { get; init; } = 60;
    public string? ReportPath { get; init; }
}

public sealed class MarketplaceVerificationReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset VerifiedAt { get; init; }
    public required Uri RegistryUri { get; init; }
    public DateTimeOffset RegistryGeneratedAt { get; init; }
    public int EntryCount { get; init; }
    public int PackageCount { get; init; }
    public long TotalPackageBytes { get; init; }
    public int TrustedPublisherKeyCount { get; init; }
    public long DurationMilliseconds { get; init; }
    public IReadOnlyList<MarketplaceVerifiedPackage> Packages { get; init; }
        = Array.Empty<MarketplaceVerifiedPackage>();
}

public sealed class MarketplaceVerifiedPackage
{
    public string PluginId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string PublisherKeyId { get; init; } = string.Empty;
    public long Bytes { get; init; }
}

public static class VerificationArguments
{
    public static MarketplaceVerificationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument: {args[i]}");
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
            values[args[i][2..]] = args[++i];
        }
        string RequiredValue(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required argument --{key}");
        var hosts = values.GetValueOrDefault("allowed-hosts")?.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();
        var timeout = values.TryGetValue("timeout-seconds", out var timeoutValue)
            && int.TryParse(timeoutValue, out var parsedTimeout) ? parsedTimeout : 60;
        return new MarketplaceVerificationOptions
        {
            RegistryUri = new Uri(RequiredValue("registry"), UriKind.Absolute),
            TrustStorePath = RequiredValue("trust"),
            AllowedPackageHosts = hosts,
            TimeoutSeconds = timeout,
            ReportPath = values.GetValueOrDefault("report"),
        };
    }
}
