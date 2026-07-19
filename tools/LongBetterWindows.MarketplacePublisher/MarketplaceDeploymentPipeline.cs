using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceDeploymentPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<MarketplaceDeploymentResult> DeployAsync(
        MarketplaceDeploymentOptions options,
        IMarketplaceDeploymentTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await CreatePlanAsync(options.BundleDirectory, cancellationToken);
        if (options.DryRun)
        {
            var dryRunResult = new MarketplaceDeploymentResult { Plan = plan, DryRun = true };
            await WriteExecutionReportAsync(options, dryRunResult, cancellationToken);
            return dryRunResult;
        }

        var ownsTarget = target == null;
        target ??= options.TargetKind switch
        {
            MarketplaceDeploymentTargetKind.LocalDirectory => new LocalDirectoryDeploymentTarget(
                Required(options.LocalTargetDirectory, nameof(options.LocalTargetDirectory)), options.Force),
            MarketplaceDeploymentTargetKind.Https => CreateHttpTarget(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.TargetKind)),
        };
        try
        {
            await WriteExecutionReportAsync(
                options, new MarketplaceDeploymentResult { Plan = plan, DryRun = false },
                cancellationToken, "prepared");
            await target.DeployAsync(plan, cancellationToken);
            var result = new MarketplaceDeploymentResult { Plan = plan, DryRun = false };
            await WriteExecutionReportAsync(options, result, cancellationToken);
            return result;
        }
        finally
        {
            if (ownsTarget && target is IDisposable disposable) disposable.Dispose();
        }
    }

    private static async Task WriteExecutionReportAsync(
        MarketplaceDeploymentOptions options, MarketplaceDeploymentResult result,
        CancellationToken cancellationToken, string? modeOverride = null)
    {
        if (string.IsNullOrWhiteSpace(options.ResultPath)) return;
        var path = Path.GetFullPath(options.ResultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        var report = new MarketplaceDeploymentExecutionReport
        {
            SchemaVersion = 1,
            ReleaseId = result.Plan.ReleaseId,
            ExecutedAt = DateTimeOffset.UtcNow,
            Mode = modeOverride ?? (result.DryRun ? "dry_run" : "deployed"),
            Target = options.TargetKind.ToString(),
            Destination = options.TargetKind == MarketplaceDeploymentTargetKind.Https
                ? options.RemoteBaseUri?.AbsoluteUri ?? string.Empty
                : Path.GetFullPath(options.LocalTargetDirectory ?? string.Empty),
            Files = result.Plan.Files.Select(x => new MarketplaceDeploymentExecutionFile
            {
                RemotePath = x.RemotePath,
                Sha256 = x.Sha256,
                Bytes = x.Bytes,
                Kind = x.Kind.ToString(),
            }).ToArray(),
        };
        try
        {
            await File.WriteAllTextAsync(
                temporary, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    public static async Task<MarketplaceDeploymentPlan> CreatePlanAsync(
        string bundleDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(bundleDirectory);
        var registryPath = Path.Combine(root, "registry.json");
        var reportPath = Path.Combine(root, "publish-report.json");
        if (!File.Exists(registryPath) || !File.Exists(reportPath))
            throw new InvalidDataException("Bundle requires registry.json and publish-report.json.");

        var report = await ReadJsonAsync<MarketplacePublishReport>(reportPath, cancellationToken)
            ?? throw new InvalidDataException("Publish report is invalid.");
        if (report.SchemaVersion != 1 || report.Packages.Count == 0)
            throw new InvalidDataException("Publish report is empty or unsupported.");
        if (string.IsNullOrWhiteSpace(report.PublisherKeyId)
            || report.PublicKeyFingerprint.Length != 64
            || !report.PublicKeyFingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("Publish report publisher identity is invalid.");
        var catalog = await ReadJsonAsync<MarketplaceCatalog>(registryPath, cancellationToken)
            ?? throw new InvalidDataException("Registry is invalid.");
        if (catalog.SchemaVersion != 1 || catalog.Source != MarketplaceSourceKind.RemoteRegistry)
            throw new InvalidDataException("Registry must be a remote schema v1 catalog.");

        var reportFiles = new Dictionary<string, PublishedPackageReport>(StringComparer.OrdinalIgnoreCase);
        var files = new List<MarketplaceDeploymentFile>();
        foreach (var package in report.Packages)
        {
            var fullPath = ResolveWithin(root, package.File);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("Reported package is missing.", fullPath);
            var hash = await ComputeHashAsync(fullPath, cancellationToken);
            if (!HashesEqual(hash, package.Sha256) || new FileInfo(fullPath).Length != package.Bytes)
                throw new InvalidDataException($"Published package was modified after signing: {package.File}");
            var relative = NormalizeRelative(package.File);
            if (!reportFiles.TryAdd(relative, package))
                throw new InvalidDataException($"Duplicate package report path: {relative}");
            files.Add(new MarketplaceDeploymentFile
            {
                LocalPath = fullPath,
                RemotePath = relative,
                Sha256 = hash,
                Bytes = package.Bytes,
                Kind = MarketplaceDeploymentFileKind.ImmutablePackage,
            });
        }

        foreach (var entry in catalog.Entries)
        foreach (var version in entry.Versions)
        {
            if (version.PackageUri == null || !version.PackageUri.IsAbsoluteUri
                || version.PackageUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Registry package URI is not HTTPS: {entry.Id} {version.Version}");
            var fileName = Uri.UnescapeDataString(Path.GetFileName(version.PackageUri.AbsolutePath));
            var match = reportFiles.Values.SingleOrDefault(x =>
                string.Equals(Path.GetFileName(x.File), fileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.PluginId, entry.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Version, version.Version, StringComparison.OrdinalIgnoreCase));
            if (match == null || !HashesEqual(match.Sha256, version.Sha256))
                throw new InvalidDataException($"Registry does not match the signed report: {entry.Id} {version.Version}");
        }
        var registryVersions = catalog.Entries.Sum(x => x.Versions.Count);
        if (registryVersions != report.Packages.Count)
            throw new InvalidDataException("Registry and publish report package counts differ.");

        var releaseId = $"{report.GeneratedAt:yyyyMMddHHmmss}-{report.PublicKeyFingerprint[..Math.Min(12, report.PublicKeyFingerprint.Length)]}";
        var reportHash = await ComputeHashAsync(reportPath, cancellationToken);
        files.Add(new MarketplaceDeploymentFile
        {
            LocalPath = reportPath,
            RemotePath = $"releases/{releaseId}/publish-report.json",
            Sha256 = reportHash,
            Bytes = new FileInfo(reportPath).Length,
            Kind = MarketplaceDeploymentFileKind.AuditReport,
        });
        var registryHash = await ComputeHashAsync(registryPath, cancellationToken);
        files.Add(new MarketplaceDeploymentFile
        {
            LocalPath = registryPath,
            RemotePath = "registry.json",
            Sha256 = registryHash,
            Bytes = new FileInfo(registryPath).Length,
            Kind = MarketplaceDeploymentFileKind.RegistryCommit,
        });
        return new MarketplaceDeploymentPlan
        {
            BundleDirectory = root,
            ReleaseId = releaseId,
            Files = files,
        };
    }

    private static HttpMarketplaceDeploymentTarget CreateHttpTarget(MarketplaceDeploymentOptions options)
    {
        var baseUri = options.RemoteBaseUri
            ?? throw new ArgumentException("RemoteBaseUri is required for HTTPS deployment.");
        var environmentName = string.IsNullOrWhiteSpace(options.CredentialEnvironmentVariable)
            ? "LONG_MARKETPLACE_DEPLOY_TOKEN"
            : options.CredentialEnvironmentVariable;
        var token = Environment.GetEnvironmentVariable(environmentName);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Deployment credential environment variable is missing: {environmentName}");
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpMarketplaceDeploymentTarget(new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        }, baseUri, token, ownsClient: true);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static string ResolveWithin(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Deployment path must be relative.");
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Deployment path escapes the bundle directory.");
        return fullPath;
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');
    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
    private static bool HashesEqual(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch { return false; }
    }
    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value;
}

public interface IMarketplaceDeploymentTarget
{
    Task DeployAsync(MarketplaceDeploymentPlan plan, CancellationToken cancellationToken = default);
}

public sealed class LocalDirectoryDeploymentTarget : IMarketplaceDeploymentTarget
{
    private readonly string _targetDirectory;
    private readonly bool _force;
    public LocalDirectoryDeploymentTarget(string targetDirectory, bool force)
    {
        _targetDirectory = Path.GetFullPath(targetDirectory);
        _force = force;
    }

    public async Task DeployAsync(MarketplaceDeploymentPlan plan, CancellationToken cancellationToken = default)
    {
        if (IsWithin(_targetDirectory, plan.BundleDirectory) || IsWithin(plan.BundleDirectory, _targetDirectory))
            throw new InvalidDataException("Deployment target and source bundle must not contain each other.");
        var parent = Directory.GetParent(_targetDirectory)?.FullName
            ?? throw new InvalidDataException("Deployment target requires a parent directory.");
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".market-deploy-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".market-deploy-backup-{Guid.NewGuid():N}");
        var backedUp = false;
        var preserveBackup = false;
        try
        {
            Directory.CreateDirectory(stage);
            byte[]? previousRegistry = null;
            var existingRegistry = Path.Combine(_targetDirectory, "registry.json");
            if (File.Exists(existingRegistry))
                previousRegistry = await File.ReadAllBytesAsync(existingRegistry, cancellationToken);
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(stage, file.RemotePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithin(stage, destination)) throw new InvalidDataException("Deployment plan contains an unsafe path.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = File.OpenRead(file.LocalPath);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output, cancellationToken);
            }
            await MarketplaceDeploymentRecovery.WriteLocalAsync(
                stage, plan, previousRegistry, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(stage, "deployment-release.txt"), plan.ReleaseId, cancellationToken);
            if (Directory.Exists(_targetDirectory))
            {
                if (!_force) throw new IOException($"Deployment target already exists: {_targetDirectory}");
                Directory.Move(_targetDirectory, backup);
                backedUp = true;
            }
            Directory.Move(stage, _targetDirectory);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch (Exception deployError)
        {
            try
            {
                if (backedUp && !Directory.Exists(_targetDirectory) && Directory.Exists(backup))
                    Directory.Move(backup, _targetDirectory);
            }
            catch (Exception rollbackError)
            {
                preserveBackup = Directory.Exists(backup);
                throw new AggregateException("Deployment failed and rollback failed; backup preserved.", deployError, rollbackError);
            }
            throw;
        }
        finally
        {
            TryDelete(stage);
            if (!preserveBackup && (!backedUp || Directory.Exists(_targetDirectory))) TryDelete(backup);
        }
    }

    private static bool IsWithin(string directory, string candidate)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

public sealed class HttpMarketplaceDeploymentTarget : IMarketplaceDeploymentTarget, IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _baseUri;
    private readonly string _bearerToken;
    private readonly bool _ownsClient;
    public HttpMarketplaceDeploymentTarget(HttpClient client, Uri baseUri, string bearerToken, bool ownsClient = false)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps || !baseUri.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("HTTPS deployment base URI must end with '/'.", nameof(baseUri));
        _client = client;
        _baseUri = baseUri;
        _bearerToken = string.IsNullOrWhiteSpace(bearerToken)
            ? throw new ArgumentException("Bearer token is required.", nameof(bearerToken)) : bearerToken;
        _ownsClient = ownsClient;
    }

    public async Task DeployAsync(MarketplaceDeploymentPlan plan, CancellationToken cancellationToken = default)
    {
        var registryUri = new Uri(_baseUri, "registry.json");
        var previousRegistry = await TryReadExistingAsync(registryUri, cancellationToken);
        foreach (var file in plan.Files)
        {
            var uri = new Uri(_baseUri, file.RemotePath);
            if (!SameSecureOrigin(uri)) throw new InvalidDataException("Deployment path changed HTTPS origin.");
            try
            {
                if (file.Kind == MarketplaceDeploymentFileKind.RegistryCommit)
                    await UploadRecoveryArtifactsAsync(plan, previousRegistry, cancellationToken);
                await PutFileAsync(uri, file, cancellationToken);
                await VerifyAsync(uri, file, cancellationToken);
            }
            catch (Exception commitError) when (file.Kind == MarketplaceDeploymentFileKind.RegistryCommit)
            {
                try
                {
                    if (previousRegistry == null)
                        await DeleteAsync(registryUri, CancellationToken.None);
                    else
                        await PutBytesAsync(registryUri, previousRegistry, CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Registry commit failed and the previous Registry could not be restored.",
                        commitError, rollbackError);
                }
                throw;
            }
        }
    }

    private async Task UploadRecoveryArtifactsAsync(
        MarketplaceDeploymentPlan plan, byte[]? previousRegistry, CancellationToken cancellationToken)
    {
        var artifacts = MarketplaceDeploymentRecovery.Create(plan, previousRegistry);
        foreach (var artifact in artifacts)
        {
            var uri = new Uri(_baseUri, artifact.RemotePath);
            if (!SameSecureOrigin(uri)) throw new InvalidDataException("Recovery artifact changed HTTPS origin.");
            await PutBytesAsync(uri, artifact.Bytes, cancellationToken);
        }
    }

    private async Task PutFileAsync(
        Uri uri, MarketplaceDeploymentFile file, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(File.OpenRead(file.LocalPath));
        content.Headers.ContentLength = file.Bytes;
        await SendPutAsync(uri, content, file.Sha256, cancellationToken);
    }

    private async Task PutBytesAsync(Uri uri, byte[] bytes, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var content = new ByteArrayContent(bytes);
        await SendPutAsync(uri, content, hash, cancellationToken);
        using var verify = new HttpRequestMessage(HttpMethod.Get, uri);
        AddAuthorization(verify);
        using var response = await _client.SendAsync(verify, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSafeSuccess(response, uri);
        var restored = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(restored), SHA256.HashData(bytes)))
            throw new InvalidDataException("Restored Registry verification failed.");
    }

    private async Task SendPutAsync(
        Uri uri, HttpContent content, string sha256, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = content };
        AddAuthorization(request);
        request.Headers.Add("X-Content-SHA256", sha256);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSafeSuccess(response, uri);
    }

    private async Task VerifyAsync(Uri uri, MarketplaceDeploymentFile expected, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSafeSuccess(response, uri);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash), Convert.FromHexString(expected.Sha256)))
            throw new InvalidDataException($"Remote verification failed: {expected.RemotePath}");
    }

    private async Task<byte[]?> TryReadExistingAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddAuthorization(request);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSafeSuccess(response, uri);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task DeleteAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        AddAuthorization(request);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) EnsureSafeSuccess(response, uri);
    }

    private void AddAuthorization(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

    private void EnsureSafeSuccess(HttpResponseMessage response, Uri requestedUri)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Deployment redirects are not allowed.");
        if (!SameSecureOrigin(response.RequestMessage?.RequestUri ?? requestedUri))
            throw new HttpRequestException("Deployment response changed HTTPS origin.");
        response.EnsureSuccessStatusCode();
    }

    private bool SameSecureOrigin(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps && uri.IdnHost.Equals(_baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            && uri.Port == _baseUri.Port;
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}

public enum MarketplaceDeploymentTargetKind { LocalDirectory, Https }
public enum MarketplaceDeploymentFileKind { ImmutablePackage, AuditReport, RegistryCommit }

public sealed class MarketplaceDeploymentOptions
{
    public required string BundleDirectory { get; init; }
    public MarketplaceDeploymentTargetKind TargetKind { get; init; }
    public string? LocalTargetDirectory { get; init; }
    public Uri? RemoteBaseUri { get; init; }
    public string CredentialEnvironmentVariable { get; init; } = "LONG_MARKETPLACE_DEPLOY_TOKEN";
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public string? ResultPath { get; init; }
}

public sealed class MarketplaceDeploymentPlan
{
    public string BundleDirectory { get; init; } = string.Empty;
    public string ReleaseId { get; init; } = string.Empty;
    public IReadOnlyList<MarketplaceDeploymentFile> Files { get; init; } = Array.Empty<MarketplaceDeploymentFile>();
}

public sealed class MarketplaceDeploymentFile
{
    public string LocalPath { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public MarketplaceDeploymentFileKind Kind { get; init; }
}

public sealed class MarketplaceDeploymentResult
{
    public required MarketplaceDeploymentPlan Plan { get; init; }
    public bool DryRun { get; init; }
}

public sealed class MarketplaceDeploymentExecutionReport
{
    public int SchemaVersion { get; init; } = 1;
    public string ReleaseId { get; init; } = string.Empty;
    public DateTimeOffset ExecutedAt { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public IReadOnlyList<MarketplaceDeploymentExecutionFile> Files { get; init; }
        = Array.Empty<MarketplaceDeploymentExecutionFile>();
}

public sealed class MarketplaceDeploymentExecutionFile
{
    public string RemotePath { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public string Kind { get; init; } = string.Empty;
}

public static class MarketplaceDeploymentRecovery
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<MarketplaceRecoveryArtifact> Create(
        MarketplaceDeploymentPlan plan, byte[]? previousRegistry)
    {
        var registry = plan.Files.Single(x => x.Kind == MarketplaceDeploymentFileKind.RegistryCommit);
        var releaseRoot = $"releases/{plan.ReleaseId}";
        var artifacts = new List<MarketplaceRecoveryArtifact>();
        string? previousHash = null;
        if (previousRegistry is { Length: > 0 })
        {
            previousHash = Convert.ToHexString(SHA256.HashData(previousRegistry));
            artifacts.Add(new MarketplaceRecoveryArtifact
            {
                RemotePath = $"{releaseRoot}/previous-registry.json",
                Bytes = previousRegistry,
            });
        }
        var manifest = new MarketplaceDeploymentManifest
        {
            SchemaVersion = 1,
            ReleaseId = plan.ReleaseId,
            PreparedAt = DateTimeOffset.UtcNow,
            CurrentRegistrySha256 = registry.Sha256,
            PreviousRegistrySha256 = previousHash,
            PreviousRegistryPath = previousHash == null ? null : $"{releaseRoot}/previous-registry.json",
            RollbackAvailable = previousHash != null,
        };
        artifacts.Add(new MarketplaceRecoveryArtifact
        {
            RemotePath = $"{releaseRoot}/deployment-manifest.json",
            Bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
        });
        return artifacts;
    }

    public static async Task WriteLocalAsync(
        string root, MarketplaceDeploymentPlan plan, byte[]? previousRegistry,
        CancellationToken cancellationToken = default)
    {
        foreach (var artifact in Create(plan, previousRegistry))
        {
            var path = Path.GetFullPath(Path.Combine(root, artifact.RemotePath.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery artifact escaped deployment directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, artifact.Bytes, cancellationToken);
        }
    }
}

public sealed class MarketplaceRecoveryArtifact
{
    public string RemotePath { get; init; } = string.Empty;
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
}

public sealed class MarketplaceDeploymentManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string ReleaseId { get; init; } = string.Empty;
    public DateTimeOffset PreparedAt { get; init; }
    public string CurrentRegistrySha256 { get; init; } = string.Empty;
    public string? PreviousRegistrySha256 { get; init; }
    public string? PreviousRegistryPath { get; init; }
    public bool RollbackAvailable { get; init; }
}

public static class DeploymentArguments
{
    public static MarketplaceDeploymentOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")
                || string.Equals(args[i], "--force", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}");
            values[args[i][2..]] = args[++i];
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required argument --{key}");
        var target = Required("target");
        var kind = target.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? MarketplaceDeploymentTargetKind.Https
            : target.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? MarketplaceDeploymentTargetKind.LocalDirectory
                : throw new ArgumentException("--target must be Local or Https.");
        return new MarketplaceDeploymentOptions
        {
            BundleDirectory = Required("bundle"),
            TargetKind = kind,
            LocalTargetDirectory = kind == MarketplaceDeploymentTargetKind.LocalDirectory ? Required("destination") : null,
            RemoteBaseUri = kind == MarketplaceDeploymentTargetKind.Https
                ? new Uri(Required("destination"), UriKind.Absolute) : null,
            CredentialEnvironmentVariable = values.GetValueOrDefault("credential-env")
                ?? "LONG_MARKETPLACE_DEPLOY_TOKEN",
            Force = force,
            DryRun = dryRun,
            ResultPath = values.GetValueOrDefault("result"),
        };
    }
}
