using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplacePublishingPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<MarketplacePublishResult> PublishAsync(
        MarketplacePublishOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var sourcePath = Path.GetFullPath(options.SourceCatalogPath);
        var packagesRoot = Path.GetFullPath(options.PackagesDirectory);
        var outputPath = Path.GetFullPath(options.OutputDirectory);
        var source = await ReadSourceAsync(sourcePath, cancellationToken);

        var privatePem = await File.ReadAllTextAsync(
            Path.GetFullPath(options.PrivateKeyPath), cancellationToken);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePem);
        if (rsa.KeySize < 2048) throw new InvalidDataException("RSA private key must be at least 2048 bits.");
        try { _ = rsa.ExportParameters(true).D; }
        catch (CryptographicException) { throw new InvalidDataException("The PEM file does not contain an RSA private key."); }

        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        var parent = Directory.GetParent(outputPath)?.FullName
            ?? throw new InvalidDataException("Output directory must have a parent directory.");
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".market-publish-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".market-backup-{Guid.NewGuid():N}");
        var outputBackedUp = false;
        var preserveBackup = false;
        Directory.CreateDirectory(Path.Combine(stage, "packages"));

        try
        {
            var registryEntries = new List<MarketplaceEntry>();
            var reportPackages = new List<PublishedPackageReport>();
            var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidDataException("Every source entry requires Id and Name.");
                if (entry.Versions.Count == 0)
                    throw new InvalidDataException($"{entry.Id} has no versions.");
                var duplicateVersion = entry.Versions
                    .GroupBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicateVersion != null)
                    throw new InvalidDataException($"{entry.Id} contains duplicate version {duplicateVersion.Key}.");
                var versions = new List<MarketplacePackageVersion>();
                foreach (var version in entry.Versions)
                {
                    var sourcePackage = ResolveWithin(packagesRoot, version.PackageFile);
                    if (!File.Exists(sourcePackage) || !sourcePackage.EndsWith(".lpak", StringComparison.OrdinalIgnoreCase))
                        throw new FileNotFoundException("LPAK package was not found.", sourcePackage);

                    var validation = await new PluginPackageValidator().ValidateAsync(
                        sourcePackage,
                        new MarketplacePackageMetadata
                        {
                            ExpectedPluginId = entry.Id,
                            ExpectedVersion = version.Version,
                        });
                    if (!validation.IsSuccess)
                        throw new InvalidDataException($"{entry.Id} {version.Version}: {validation.Error}");

                    var hashBytes = Convert.FromHexString(validation.Sha256!);
                    var signature = rsa.SignHash(
                        hashBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    var outputName = $"{Sanitize(entry.Id)}-v{Sanitize(version.Version)}-{validation.Sha256![..12]}.lpak";
                    if (!outputNames.Add(outputName))
                        throw new InvalidDataException($"Published package name collision: {outputName}");
                    var outputPackage = Path.Combine(stage, "packages", outputName);
                    File.Copy(sourcePackage, outputPackage, true);
                    var packageUri = new Uri(options.BasePackageUri, Uri.EscapeDataString(outputName));
                    var manifest = validation.Manifest!;
                    versions.Add(new MarketplacePackageVersion
                    {
                        Version = manifest.Version,
                        PackageUri = packageUri,
                        Sha256 = validation.Sha256!,
                        Signature = Convert.ToBase64String(signature),
                        PublisherPublicKeyPem = publicKeyPem,
                        PublisherKeyId = options.PublisherKeyId,
                        PublishedAt = version.PublishedAt ?? DateTimeOffset.UtcNow,
                        ReleaseNotes = version.ReleaseNotes,
                        Capabilities = manifest.Capabilities,
                        MinHostVersion = manifest.MinHostVersion,
                        MinApiVersion = manifest.MinApiVersion,
                        MinUiKitVersion = manifest.MinUiKitVersion,
                    });
                    reportPackages.Add(new PublishedPackageReport
                    {
                        PluginId = entry.Id,
                        Version = manifest.Version,
                        File = $"packages/{outputName}",
                        Sha256 = validation.Sha256!,
                        Bytes = new FileInfo(outputPackage).Length,
                    });
                }

                registryEntries.Add(new MarketplaceEntry
                {
                    Source = MarketplaceSourceKind.RemoteRegistry,
                    Id = entry.Id,
                    Name = entry.Name,
                    Summary = entry.Summary,
                    Description = entry.Description,
                    Publisher = options.PublisherName,
                    Category = entry.Category,
                    Tags = entry.Tags,
                    Versions = versions.OrderByDescending(x => ParseVersion(x.Version)).ToArray(),
                });
            }

            var generatedAt = DateTimeOffset.UtcNow;
            await WriteJsonAsync(Path.Combine(stage, "registry.json"), new MarketplaceCatalog
            {
                SchemaVersion = 1,
                Source = MarketplaceSourceKind.RemoteRegistry,
                GeneratedAt = generatedAt,
                Entries = registryEntries.OrderBy(x => x.Name).ToArray(),
            }, cancellationToken);
            await WriteJsonAsync(Path.Combine(stage, "trusted-publisher.fragment.json"),
                new TrustedPublisherDocument
                {
                    SchemaVersion = 1,
                    Publishers = new[]
                    {
                        new TrustedPublisherKey
                        {
                            KeyId = options.PublisherKeyId,
                            Publisher = options.PublisherName,
                            Algorithm = "RSA-SHA256",
                            PublicKeyPem = publicKeyPem,
                            Sha256Fingerprint = fingerprint,
                        },
                    },
                }, cancellationToken);
            await WriteJsonAsync(Path.Combine(stage, "publish-report.json"), new MarketplacePublishReport
            {
                SchemaVersion = 1,
                GeneratedAt = generatedAt,
                Publisher = options.PublisherName,
                PublisherKeyId = options.PublisherKeyId,
                PublicKeyFingerprint = fingerprint,
                Packages = reportPackages,
            }, cancellationToken);

            if (Directory.Exists(outputPath))
            {
                if (!options.Force) throw new IOException($"Output directory already exists: {outputPath}");
                Directory.Move(outputPath, backup);
                outputBackedUp = true;
            }
            Directory.Move(stage, outputPath);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            return new MarketplacePublishResult
            {
                OutputDirectory = outputPath,
                PackageCount = reportPackages.Count,
                PublisherKeyId = options.PublisherKeyId,
                PublicKeyFingerprint = fingerprint,
            };
        }
        catch (Exception publishError)
        {
            try
            {
                if (outputBackedUp && !Directory.Exists(outputPath) && Directory.Exists(backup))
                    Directory.Move(backup, outputPath);
            }
            catch (Exception rollbackError)
            {
                preserveBackup = Directory.Exists(backup);
                throw new AggregateException(
                    "Marketplace publish failed and the previous output could not be restored. The backup was preserved.",
                    publishError, rollbackError);
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stage);
            if (!preserveBackup && (!outputBackedUp || Directory.Exists(outputPath)))
                TryDeleteDirectory(backup);
        }
    }

    private static async Task<MarketplacePublishSource> ReadSourceAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var source = await JsonSerializer.DeserializeAsync<MarketplacePublishSource>(
            stream, JsonOptions, cancellationToken);
        if (source?.SchemaVersion != 1 || source.Entries.Count == 0)
            throw new InvalidDataException("Source catalog is empty or has an unsupported schema version.");
        var duplicate = source.Entries.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate != null) throw new InvalidDataException($"Duplicate plugin ID: {duplicate.Key}");
        return source;
    }

    private static void ValidateOptions(MarketplacePublishOptions options)
    {
        if (!File.Exists(options.SourceCatalogPath)) throw new FileNotFoundException("Source catalog not found.");
        if (!Directory.Exists(options.PackagesDirectory)) throw new DirectoryNotFoundException("Packages directory not found.");
        if (!File.Exists(options.PrivateKeyPath)) throw new FileNotFoundException("Private key not found.");
        if (string.IsNullOrWhiteSpace(options.PublisherKeyId) || string.IsNullOrWhiteSpace(options.PublisherName))
            throw new ArgumentException("Publisher key ID and publisher name are required.");
        if (!options.BasePackageUri.IsAbsoluteUri || options.BasePackageUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Base package URI must use HTTPS.");
        if (!options.BasePackageUri.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("Base package URI must end with '/'.");

        var output = Path.GetFullPath(options.OutputDirectory);
        var source = Path.GetFullPath(options.SourceCatalogPath);
        var packages = Path.GetFullPath(options.PackagesDirectory);
        var privateKey = Path.GetFullPath(options.PrivateKeyPath);
        if (IsWithin(output, source) || IsWithin(output, packages) || IsWithin(output, privateKey))
            throw new InvalidDataException(
                "Output directory must not contain the source catalog, packages directory or private key.");
    }

    private static bool IsWithin(string directory, string candidate)
    {
        var root = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("PackageFile must be a relative path.");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PackageFile escapes the packages directory.");
        return fullPath;
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static Version ParseVersion(string value)
        => Version.TryParse(value.TrimStart('v', 'V'), out var parsed) ? parsed : new Version(0, 0, 0);

    private static string Sanitize(string value)
        => new(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}

public sealed class MarketplacePublishOptions
{
    public required string SourceCatalogPath { get; init; }
    public required string PackagesDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string PrivateKeyPath { get; init; }
    public required string PublisherKeyId { get; init; }
    public required string PublisherName { get; init; }
    public required Uri BasePackageUri { get; init; }
    public bool Force { get; init; }
}

public sealed class MarketplacePublishSource
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<MarketplacePublishEntry> Entries { get; init; } = Array.Empty<MarketplacePublishEntry>();
}

public sealed class MarketplacePublishEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MarketplacePublishVersion> Versions { get; init; } = Array.Empty<MarketplacePublishVersion>();
}

public sealed class MarketplacePublishVersion
{
    public string Version { get; init; } = string.Empty;
    public string PackageFile { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
}

public sealed class MarketplacePublishReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; }
    public string Publisher { get; init; } = string.Empty;
    public string PublisherKeyId { get; init; } = string.Empty;
    public string PublicKeyFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<PublishedPackageReport> Packages { get; init; } = Array.Empty<PublishedPackageReport>();
}

public sealed class PublishedPackageReport
{
    public string PluginId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
}

public sealed class MarketplacePublishResult
{
    public string OutputDirectory { get; init; } = string.Empty;
    public int PackageCount { get; init; }
    public string PublisherKeyId { get; init; } = string.Empty;
    public string PublicKeyFingerprint { get; init; } = string.Empty;
}

public static class PublisherArguments
{
    public static MarketplacePublishOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var force = args.Any(x => string.Equals(x, "--force", StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--") || string.Equals(args[i], "--force", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
            values[args[i][2..]] = args[++i];
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required argument --{key}");
        return new MarketplacePublishOptions
        {
            SourceCatalogPath = Required("source"),
            PackagesDirectory = Required("packages"),
            OutputDirectory = Required("output"),
            PrivateKeyPath = Required("private-key"),
            PublisherKeyId = Required("key-id"),
            PublisherName = Required("publisher"),
            BasePackageUri = new Uri(Required("base-uri"), UriKind.Absolute),
            Force = force,
        };
    }
}
