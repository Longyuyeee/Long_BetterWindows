using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.MarketplacePublisher;

public sealed class MarketplaceRollbackPipeline
{
    public async Task<MarketplaceRollbackResult> RollbackAsync(
        MarketplaceRollbackOptions options,
        IMarketplaceRollbackTarget? target = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var releaseId = ValidateReleaseId(options.ReleaseId);
        if (!string.Equals(releaseId, options.ConfirmReleaseId, StringComparison.Ordinal))
            throw new InvalidOperationException("Rollback requires --confirm-release to exactly match --release.");

        var ownsTarget = target == null;
        target ??= options.TargetKind switch
        {
            MarketplaceDeploymentTargetKind.LocalDirectory => new LocalMarketplaceRollbackTarget(
                options.LocalTargetDirectory ?? throw new ArgumentException("Local target directory is required.")),
            MarketplaceDeploymentTargetKind.Https => CreateHttpTarget(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options.TargetKind)),
        };
        try
        {
            return await target.RollbackAsync(releaseId, cancellationToken);
        }
        finally
        {
            if (ownsTarget && target is IDisposable disposable) disposable.Dispose();
        }
    }

    private static HttpMarketplaceRollbackTarget CreateHttpTarget(MarketplaceRollbackOptions options)
    {
        var baseUri = options.RemoteBaseUri
            ?? throw new ArgumentException("Remote base URI is required.");
        var environmentName = string.IsNullOrWhiteSpace(options.CredentialEnvironmentVariable)
            ? "LONG_MARKETPLACE_DEPLOY_TOKEN"
            : options.CredentialEnvironmentVariable;
        var token = Environment.GetEnvironmentVariable(environmentName);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Rollback credential environment variable is missing: {environmentName}");
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpMarketplaceRollbackTarget(new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        }, baseUri, token, ownsClient: true);
    }

    internal static string ValidateReleaseId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96
            || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Release ID contains unsafe characters.");
        return value;
    }
}

public interface IMarketplaceRollbackTarget
{
    Task<MarketplaceRollbackResult> RollbackAsync(
        string releaseId, CancellationToken cancellationToken = default);
}

public sealed class LocalMarketplaceRollbackTarget : IMarketplaceRollbackTarget
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _root;
    public LocalMarketplaceRollbackTarget(string root) => _root = Path.GetFullPath(root);

    public async Task<MarketplaceRollbackResult> RollbackAsync(
        string releaseId, CancellationToken cancellationToken = default)
    {
        var releaseRoot = Path.Combine(_root, "releases", releaseId);
        var manifestPath = Path.Combine(releaseRoot, "deployment-manifest.json");
        var snapshotPath = Path.Combine(releaseRoot, "previous-registry.json");
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        var snapshot = await File.ReadAllBytesAsync(snapshotPath, cancellationToken);
        ValidateSnapshot(manifest, releaseId, snapshot);

        var registryPath = Path.Combine(_root, "registry.json");
        var current = File.Exists(registryPath)
            ? await File.ReadAllBytesAsync(registryPath, cancellationToken) : null;
        var temporary = registryPath + $".{Guid.NewGuid():N}.rollback";
        try
        {
            await File.WriteAllBytesAsync(temporary, snapshot, cancellationToken);
            File.Move(temporary, registryPath, true);
            var restored = await File.ReadAllBytesAsync(registryPath, cancellationToken);
            if (!HashesEqual(restored, snapshot)) throw new InvalidDataException("Local Registry rollback verification failed.");
        }
        catch
        {
            if (current == null)
            {
                try { if (File.Exists(registryPath)) File.Delete(registryPath); } catch { }
            }
            else
            {
                try { await File.WriteAllBytesAsync(registryPath, current, CancellationToken.None); } catch { }
            }
            throw;
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }

        return MarketplaceRollbackResult.Create(releaseId, manifest.CurrentRegistrySha256, manifest.PreviousRegistrySha256!);
    }

    internal static async Task<MarketplaceDeploymentManifest> ReadManifestAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Deployment manifest was not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MarketplaceDeploymentManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Deployment manifest is invalid.");
    }

    internal static void ValidateSnapshot(
        MarketplaceDeploymentManifest manifest, string releaseId, byte[] snapshot)
    {
        if (manifest.SchemaVersion != 1 || !manifest.RollbackAvailable
            || !string.Equals(manifest.ReleaseId, releaseId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.PreviousRegistrySha256))
            throw new InvalidDataException("Deployment does not contain a valid rollback point.");
        var actual = Convert.ToHexString(SHA256.HashData(snapshot));
        if (!string.Equals(actual, manifest.PreviousRegistrySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Previous Registry snapshot hash does not match the deployment manifest.");
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
            };
            var catalog = JsonSerializer.Deserialize<MarketplaceCatalog>(snapshot, options);
            if (catalog?.SchemaVersion != 1 || catalog.Source != MarketplaceSourceKind.RemoteRegistry
                || catalog.Entries.Count == 0)
                throw new InvalidDataException("Previous Registry snapshot is not a valid remote catalog.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Previous Registry snapshot JSON is invalid.", ex);
        }
    }

    private static bool HashesEqual(byte[] left, byte[] right)
        => CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right));
}

public sealed class HttpMarketplaceRollbackTarget : IMarketplaceRollbackTarget, IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _baseUri;
    private readonly string _token;
    private readonly bool _ownsClient;
    public HttpMarketplaceRollbackTarget(
        HttpClient client, Uri baseUri, string token, bool ownsClient = false)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps || !baseUri.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("HTTPS rollback base URI must end with '/'.", nameof(baseUri));
        _client = client;
        _baseUri = baseUri;
        _token = string.IsNullOrWhiteSpace(token) ? throw new ArgumentException("Bearer token is required.") : token;
        _ownsClient = ownsClient;
    }

    public async Task<MarketplaceRollbackResult> RollbackAsync(
        string releaseId, CancellationToken cancellationToken = default)
    {
        var releaseRoot = $"releases/{releaseId}/";
        var manifestBytes = await GetAsync(new Uri(_baseUri, releaseRoot + "deployment-manifest.json"), cancellationToken);
        var manifest = JsonSerializer.Deserialize<MarketplaceDeploymentManifest>(manifestBytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Deployment manifest is invalid.");
        var snapshot = await GetAsync(new Uri(_baseUri, releaseRoot + "previous-registry.json"), cancellationToken);
        LocalMarketplaceRollbackTarget.ValidateSnapshot(manifest, releaseId, snapshot);

        var registryUri = new Uri(_baseUri, "registry.json");
        byte[]? current = null;
        try { current = await GetAsync(registryUri, cancellationToken); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
        try
        {
            await PutAndVerifyAsync(registryUri, snapshot, cancellationToken);
        }
        catch (Exception rollbackError)
        {
            try
            {
                if (current == null) await DeleteAsync(registryUri, CancellationToken.None);
                else await PutAndVerifyAsync(registryUri, current, CancellationToken.None);
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    "Registry rollback failed and the pre-rollback Registry could not be restored.",
                    rollbackError, restoreError);
            }
            throw;
        }
        return MarketplaceRollbackResult.Create(releaseId, manifest.CurrentRegistrySha256, manifest.PreviousRegistrySha256!);
    }

    private async Task<byte[]> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        EnsureOrigin(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        AddAuthorization(request);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response, uri);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task PutAndVerifyAsync(Uri uri, byte[] bytes, CancellationToken cancellationToken)
    {
        EnsureOrigin(uri);
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = new ByteArrayContent(bytes) };
        AddAuthorization(request);
        request.Headers.Add("X-Content-SHA256", Convert.ToHexString(SHA256.HashData(bytes)));
        using var response = await _client.SendAsync(request, cancellationToken);
        EnsureSuccess(response, uri);
        var restored = await GetAsync(uri, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(restored), SHA256.HashData(bytes)))
            throw new InvalidDataException("Remote Registry rollback verification failed.");
    }

    private async Task DeleteAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        AddAuthorization(request);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) EnsureSuccess(response, uri);
    }

    private void AddAuthorization(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    private void EnsureOrigin(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || !uri.IdnHost.Equals(_baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || uri.Port != _baseUri.Port)
            throw new HttpRequestException("Rollback URI changed HTTPS origin.");
    }
    private void EnsureSuccess(HttpResponseMessage response, Uri requestedUri)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Rollback redirects are not allowed.");
        EnsureOrigin(response.RequestMessage?.RequestUri ?? requestedUri);
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}

public sealed class MarketplaceRollbackOptions
{
    public MarketplaceDeploymentTargetKind TargetKind { get; init; }
    public string? LocalTargetDirectory { get; init; }
    public Uri? RemoteBaseUri { get; init; }
    public string ReleaseId { get; init; } = string.Empty;
    public string ConfirmReleaseId { get; init; } = string.Empty;
    public string CredentialEnvironmentVariable { get; init; } = "LONG_MARKETPLACE_DEPLOY_TOKEN";
}

public sealed class MarketplaceRollbackResult
{
    public string ReleaseId { get; init; } = string.Empty;
    public string ReplacedRegistrySha256 { get; init; } = string.Empty;
    public string RestoredRegistrySha256 { get; init; } = string.Empty;
    public DateTimeOffset RolledBackAt { get; init; }
    public static MarketplaceRollbackResult Create(string releaseId, string replaced, string restored)
        => new()
        {
            ReleaseId = releaseId,
            ReplacedRegistrySha256 = replaced,
            RestoredRegistrySha256 = restored,
            RolledBackAt = DateTimeOffset.UtcNow,
        };
}

public static class RollbackArguments
{
    public static MarketplaceRollbackOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ArgumentException($"Invalid rollback argument: {args[i]}");
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
        return new MarketplaceRollbackOptions
        {
            TargetKind = kind,
            LocalTargetDirectory = kind == MarketplaceDeploymentTargetKind.LocalDirectory ? Required("destination") : null,
            RemoteBaseUri = kind == MarketplaceDeploymentTargetKind.Https
                ? new Uri(Required("destination"), UriKind.Absolute) : null,
            ReleaseId = Required("release"),
            ConfirmReleaseId = Required("confirm-release"),
            CredentialEnvironmentVariable = values.GetValueOrDefault("credential-env")
                ?? "LONG_MARKETPLACE_DEPLOY_TOKEN",
        };
    }
}
