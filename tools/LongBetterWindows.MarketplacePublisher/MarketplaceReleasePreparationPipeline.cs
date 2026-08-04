using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceReleasePreparationPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<MarketplaceReleasePreparationReport> PrepareAsync(
        MarketplaceReleasePreparationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bundleDirectory = Path.GetFullPath(options.BundleDirectory);
        var evidenceDirectory = Path.GetFullPath(options.EvidenceDirectory);
        ValidateOutputPath(bundleDirectory, nameof(options.BundleDirectory));
        ValidateOutputPath(evidenceDirectory, nameof(options.EvidenceDirectory));
        EnsureSeparate(bundleDirectory, evidenceDirectory);
        var packagesDirectory = Path.GetFullPath(options.PackagesDirectory);
        EnsureSeparate(packagesDirectory, bundleDirectory);
        EnsureSeparate(packagesDirectory, evidenceDirectory);

        var evidenceParent = Directory.GetParent(evidenceDirectory)?.FullName
            ?? throw new InvalidDataException("Evidence directory must have a parent directory.");
        Directory.CreateDirectory(evidenceParent);
        var evidenceStage = Path.Combine(
            evidenceParent,
            $".market-prepare-{Guid.NewGuid():N}");
        var bundleCreated = false;
        Directory.CreateDirectory(evidenceStage);
        try
        {
            var published = await new MarketplacePublishingPipeline().PublishAsync(
                new MarketplacePublishOptions
                {
                    SourceCatalogPath = options.SourceCatalogPath,
                    PackagesDirectory = options.PackagesDirectory,
                    OutputDirectory = bundleDirectory,
                    PrivateKeyPath = options.PrivateKeyPath,
                    PublisherKeyId = options.PublisherKeyId,
                    PublisherName = options.PublisherName,
                    BasePackageUri = options.BasePackageUri,
                },
                cancellationToken);
            bundleCreated = true;

            var verificationPath = Path.Combine(
                evidenceStage,
                "bundle-verification.json");
            var verification = await new MarketplaceBundleVerificationPipeline().VerifyAsync(
                new MarketplaceBundleVerificationOptions
                {
                    BundleDirectory = bundleDirectory,
                    ExpectedPublisherKeyId = published.PublisherKeyId,
                    ExpectedPublicKeyFingerprint = published.PublicKeyFingerprint,
                    ReportPath = verificationPath,
                },
                cancellationToken);

            var dryRunPath = Path.Combine(evidenceStage, "deployment-dry-run.json");
            var deployment = await new MarketplaceDeploymentPipeline().DeployAsync(
                new MarketplaceDeploymentOptions
                {
                    BundleDirectory = bundleDirectory,
                    TargetKind = options.TargetKind,
                    LocalTargetDirectory = options.TargetKind
                        == MarketplaceDeploymentTargetKind.LocalDirectory
                            ? options.Destination
                            : null,
                    RemoteBaseUri = options.TargetKind == MarketplaceDeploymentTargetKind.Https
                        ? new Uri(options.Destination, UriKind.Absolute)
                        : null,
                    DryRun = true,
                    ResultPath = dryRunPath,
                },
                cancellationToken: cancellationToken);

            var registryPath = Path.Combine(bundleDirectory, "registry.json");
            var report = new MarketplaceReleasePreparationReport
            {
                SchemaVersion = 1,
                PreparedAt = DateTimeOffset.UtcNow,
                ReleaseId = deployment.Plan.ReleaseId,
                BundleDirectory = bundleDirectory,
                Target = options.TargetKind.ToString(),
                Destination = NormalizeDestination(options),
                Publisher = verification.Publisher,
                PublisherKeyId = verification.PublisherKeyId,
                PublicKeyFingerprint = verification.PublicKeyFingerprint,
                PackageCount = verification.PackageCount,
                RegistrySha256 = await ComputeHashAsync(registryPath, cancellationToken),
                BundleVerificationReportSha256 = await ComputeHashAsync(
                    verificationPath,
                    cancellationToken),
                DeploymentDryRunReportSha256 = await ComputeHashAsync(
                    dryRunPath,
                    cancellationToken),
                Files = deployment.Plan.Files.Select(file =>
                    new MarketplaceReleasePreparationFile
                    {
                        RemotePath = file.RemotePath,
                        Sha256 = file.Sha256,
                        Bytes = file.Bytes,
                        Kind = file.Kind.ToString(),
                    }).ToArray(),
            };
            await File.WriteAllTextAsync(
                Path.Combine(evidenceStage, "preparation-summary.json"),
                JsonSerializer.Serialize(report, JsonOptions),
                cancellationToken);
            Directory.Move(evidenceStage, evidenceDirectory);
            return report;
        }
        catch
        {
            TryDelete(evidenceStage);
            if (bundleCreated) TryDelete(bundleDirectory);
            throw;
        }
    }

    private static void ValidateOutputPath(string path, string name)
    {
        if (Directory.Exists(path) || File.Exists(path))
            throw new IOException($"{name} already exists: {path}");
    }

    private static void EnsureSeparate(string left, string right)
    {
        var leftRoot = left.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rightRoot = right.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (leftRoot.StartsWith(rightRoot, StringComparison.OrdinalIgnoreCase)
            || rightRoot.StartsWith(leftRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bundle and evidence directories must not contain each other.");
    }

    private static string NormalizeDestination(MarketplaceReleasePreparationOptions options)
        => options.TargetKind == MarketplaceDeploymentTargetKind.Https
            ? new Uri(options.Destination, UriKind.Absolute).AbsoluteUri
            : Path.GetFullPath(options.Destination);

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}

public sealed class MarketplaceReleasePreparationOptions
{
    public required string SourceCatalogPath { get; init; }
    public required string PackagesDirectory { get; init; }
    public required string BundleDirectory { get; init; }
    public required string EvidenceDirectory { get; init; }
    public required string PrivateKeyPath { get; init; }
    public required string PublisherKeyId { get; init; }
    public required string PublisherName { get; init; }
    public required Uri BasePackageUri { get; init; }
    public MarketplaceDeploymentTargetKind TargetKind { get; init; }
    public required string Destination { get; init; }
}

public sealed class MarketplaceReleasePreparationReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset PreparedAt { get; init; }
    public string ReleaseId { get; init; } = string.Empty;
    public string BundleDirectory { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string PublisherKeyId { get; init; } = string.Empty;
    public string PublicKeyFingerprint { get; init; } = string.Empty;
    public int PackageCount { get; init; }
    public string RegistrySha256 { get; init; } = string.Empty;
    public string BundleVerificationReportSha256 { get; init; } = string.Empty;
    public string DeploymentDryRunReportSha256 { get; init; } = string.Empty;
    public IReadOnlyList<MarketplaceReleasePreparationFile> Files { get; init; }
        = Array.Empty<MarketplaceReleasePreparationFile>();
}

public sealed class MarketplaceReleasePreparationFile
{
    public string RemotePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public string Kind { get; init; } = string.Empty;
}

public static class ReleasePreparationArguments
{
    public static MarketplaceReleasePreparationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= args.Length)
                throw new ArgumentException($"Unexpected or incomplete argument: {args[index]}");
            values[args[index][2..]] = args[++index];
        }

        string Required(string key)
            => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Missing required argument --{key}");
        var target = Required("target");
        var targetKind = target.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? MarketplaceDeploymentTargetKind.Https
            : target.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? MarketplaceDeploymentTargetKind.LocalDirectory
                : throw new ArgumentException("--target must be Local or Https.");

        return new MarketplaceReleasePreparationOptions
        {
            SourceCatalogPath = Required("source"),
            PackagesDirectory = Required("packages"),
            BundleDirectory = Required("bundle"),
            EvidenceDirectory = Required("evidence"),
            PrivateKeyPath = Required("private-key"),
            PublisherKeyId = Required("key-id"),
            PublisherName = Required("publisher"),
            BasePackageUri = new Uri(Required("base-uri"), UriKind.Absolute),
            TargetKind = targetKind,
            Destination = Required("destination"),
        };
    }
}
