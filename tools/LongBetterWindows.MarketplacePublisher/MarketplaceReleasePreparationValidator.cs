using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceReleasePreparationValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<MarketplaceReleasePreparationReport> ValidateAsync(
        MarketplaceReleasePreparationValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bundleDirectory = Path.GetFullPath(options.BundleDirectory);
        var evidenceDirectory = Path.GetFullPath(options.EvidenceDirectory);
        var summary = await ReadAsync<MarketplaceReleasePreparationReport>(
            Path.Combine(evidenceDirectory, "preparation-summary.json"),
            cancellationToken)
            ?? throw new InvalidDataException("Marketplace preparation summary is invalid.");
        if (summary.SchemaVersion != 1)
            throw new InvalidDataException("Marketplace preparation summary schema is unsupported.");
        if (!string.Equals(summary.ReleaseId, options.ConfirmReleaseId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(summary.ReleaseId))
            throw new InvalidOperationException(
                "Confirmed Release ID must exactly match the preparation summary.");
        if (!PathsEqual(summary.BundleDirectory, bundleDirectory))
            throw new InvalidDataException("Preparation summary references a different bundle directory.");

        var expectedTarget = options.TargetKind.ToString();
        var expectedDestination = NormalizeDestination(options);
        if (!string.Equals(summary.Target, expectedTarget, StringComparison.Ordinal)
            || !string.Equals(summary.Destination, expectedDestination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Preparation target differs from the approved deployment target.");

        var verificationPath = Path.Combine(evidenceDirectory, "bundle-verification.json");
        var dryRunPath = Path.Combine(evidenceDirectory, "deployment-dry-run.json");
        await RequireHashAsync(
            Path.Combine(bundleDirectory, "registry.json"),
            summary.RegistrySha256,
            "Registry",
            cancellationToken);
        await RequireHashAsync(
            verificationPath,
            summary.BundleVerificationReportSha256,
            "Bundle verification report",
            cancellationToken);
        await RequireHashAsync(
            dryRunPath,
            summary.DeploymentDryRunReportSha256,
            "Deployment Dry Run report",
            cancellationToken);

        var verification = await ReadAsync<MarketplaceBundleVerificationReport>(
            verificationPath,
            cancellationToken)
            ?? throw new InvalidDataException("Bundle verification report is invalid.");
        if (!string.Equals(verification.PublisherKeyId, summary.PublisherKeyId, StringComparison.Ordinal)
            || !string.Equals(
                verification.PublicKeyFingerprint,
                summary.PublicKeyFingerprint,
                StringComparison.OrdinalIgnoreCase)
            || verification.PackageCount != summary.PackageCount)
            throw new InvalidDataException("Bundle verification identity differs from the preparation summary.");
        var currentVerification = await new MarketplaceBundleVerificationPipeline().VerifyAsync(
            new MarketplaceBundleVerificationOptions
            {
                BundleDirectory = bundleDirectory,
                ExpectedPublisherKeyId = summary.PublisherKeyId,
                ExpectedPublicKeyFingerprint = summary.PublicKeyFingerprint,
            },
            cancellationToken);
        RequireSamePackages(verification.Packages, currentVerification.Packages);

        var dryRun = await ReadAsync<MarketplaceDeploymentExecutionReport>(
            dryRunPath,
            cancellationToken)
            ?? throw new InvalidDataException("Deployment Dry Run report is invalid.");
        if (!string.Equals(dryRun.Mode, "dry_run", StringComparison.Ordinal)
            || !string.Equals(dryRun.ReleaseId, summary.ReleaseId, StringComparison.Ordinal)
            || !string.Equals(dryRun.Target, expectedTarget, StringComparison.Ordinal)
            || !string.Equals(dryRun.Destination, expectedDestination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Deployment Dry Run does not match the approved preparation.");

        var plan = await MarketplaceDeploymentPipeline.CreatePlanAsync(
            bundleDirectory,
            cancellationToken);
        if (!string.Equals(plan.ReleaseId, summary.ReleaseId, StringComparison.Ordinal))
            throw new InvalidDataException("Bundle Release ID differs from the preparation summary.");
        RequireSameFiles(summary.Files, plan.Files, "Preparation summary");
        RequireSameFiles(dryRun.Files, plan.Files, "Deployment Dry Run");
        return summary;
    }

    private static void RequireSamePackages(
        IReadOnlyList<MarketplaceBundleVerifiedPackage> prepared,
        IReadOnlyList<MarketplaceBundleVerifiedPackage> current)
    {
        var left = prepared.OrderBy(package => package.PluginId, StringComparer.Ordinal)
            .ThenBy(package => package.Version, StringComparer.Ordinal)
            .ToArray();
        var right = current.OrderBy(package => package.PluginId, StringComparer.Ordinal)
            .ThenBy(package => package.Version, StringComparer.Ordinal)
            .ToArray();
        if (left.Length != right.Length)
            throw new InvalidDataException("Current bundle package count differs from the preparation verification.");
        for (var index = 0; index < right.Length; index++)
        {
            if (!string.Equals(left[index].PluginId, right[index].PluginId, StringComparison.Ordinal)
                || !string.Equals(left[index].Version, right[index].Version, StringComparison.Ordinal)
                || !string.Equals(left[index].File, right[index].File, StringComparison.Ordinal)
                || !HashesEqual(left[index].Sha256, right[index].Sha256)
                || left[index].Bytes != right[index].Bytes)
                throw new InvalidDataException(
                    $"Current bundle package differs from the preparation verification at index {index}.");
        }
    }

    private static void RequireSameFiles(
        IReadOnlyList<MarketplaceReleasePreparationFile> actual,
        IReadOnlyList<MarketplaceDeploymentFile> expected,
        string label)
    {
        if (actual.Count != expected.Count)
            throw new InvalidDataException($"{label} file count differs from the bundle plan.");
        for (var index = 0; index < expected.Count; index++)
        {
            var left = actual[index];
            var right = expected[index];
            if (!string.Equals(left.RemotePath, right.RemotePath, StringComparison.Ordinal)
                || !HashesEqual(left.Sha256, right.Sha256)
                || left.Bytes != right.Bytes
                || !string.Equals(left.Kind, right.Kind.ToString(), StringComparison.Ordinal))
                throw new InvalidDataException($"{label} file plan differs at index {index}.");
        }
    }

    private static void RequireSameFiles(
        IReadOnlyList<MarketplaceDeploymentExecutionFile> actual,
        IReadOnlyList<MarketplaceDeploymentFile> expected,
        string label)
    {
        var projected = actual.Select(file => new MarketplaceReleasePreparationFile
        {
            RemotePath = file.RemotePath,
            Sha256 = file.Sha256,
            Bytes = file.Bytes,
            Kind = file.Kind,
        }).ToArray();
        RequireSameFiles(projected, expected, label);
    }

    private static async Task RequireHashAsync(
        string path,
        string expected,
        string label,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} is missing.", path);
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!HashesEqual(actual, expected))
            throw new InvalidDataException($"{label} changed after release preparation.");
    }

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Preparation evidence is missing.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static string NormalizeDestination(
        MarketplaceReleasePreparationValidationOptions options)
        => options.TargetKind == MarketplaceDeploymentTargetKind.Https
            ? new Uri(options.Destination, UriKind.Absolute).AbsoluteUri
            : Path.GetFullPath(options.Destination);

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool HashesEqual(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch { return false; }
    }
}

public sealed class MarketplaceReleasePreparationValidationOptions
{
    public required string BundleDirectory { get; init; }
    public required string EvidenceDirectory { get; init; }
    public MarketplaceDeploymentTargetKind TargetKind { get; init; }
    public required string Destination { get; init; }
    public required string ConfirmReleaseId { get; init; }
}

public static class ReleasePreparationValidationArguments
{
    public static MarketplaceReleasePreparationValidationOptions Parse(string[] args)
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
        return new MarketplaceReleasePreparationValidationOptions
        {
            BundleDirectory = Required("bundle"),
            EvidenceDirectory = Required("evidence"),
            TargetKind = targetKind,
            Destination = Required("destination"),
            ConfirmReleaseId = Required("confirm-release"),
        };
    }
}
