using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public interface IMarketplaceRepository
    {
        Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MarketplaceEntry>> SearchAsync(
            string? query = null, string? category = null,
            CancellationToken cancellationToken = default);
    }

    internal static class MarketplaceCatalogCodec
    {
        public const long MaximumCatalogBytes = 2L * 1024 * 1024;
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        };

        public static async Task<MarketplaceCatalogResult> ReadAsync(
            Stream stream, CancellationToken cancellationToken,
            MarketplaceSourceKind? forcedSource = null)
        {
            try
            {
                var catalog = await JsonSerializer.DeserializeAsync<MarketplaceCatalog>(
                    stream, JsonOptions, cancellationToken);
                if (catalog == null || catalog.SchemaVersion != 1)
                    return MarketplaceCatalogResult.Fail("市场目录版本不受支持。");
                if (catalog.Entries.Count > 5000)
                    return MarketplaceCatalogResult.Fail("市场目录条目超过安全限制。");
                var duplicate = catalog.Entries
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicate != null)
                    return MarketplaceCatalogResult.Fail($"市场目录包含重复插件 ID：{duplicate.Key}");
                if (catalog.Entries.Any(x => string.IsNullOrWhiteSpace(x.Id)
                    || string.IsNullOrWhiteSpace(x.Name) || x.Versions.Count == 0))
                    return MarketplaceCatalogResult.Fail("市场目录包含不完整插件条目。");
                if (forcedSource.HasValue)
                    catalog = CloneWithSource(catalog, forcedSource.Value);
                return MarketplaceCatalogResult.Ok(catalog);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return MarketplaceCatalogResult.Fail($"市场目录不可用：{ex.Message}");
            }
        }

        public static IReadOnlyList<MarketplaceEntry> Search(
            IEnumerable<MarketplaceEntry> entries, string? query, string? category)
        {
            var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return entries
                .Where(x => string.IsNullOrWhiteSpace(category)
                    || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(x => terms.All(term => Matches(x, term)))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        private static bool Matches(MarketplaceEntry entry, string term)
            => entry.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || entry.Summary.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || entry.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || entry.Tags.Any(x => x.Contains(term, StringComparison.CurrentCultureIgnoreCase));

        private static MarketplaceCatalog CloneWithSource(
            MarketplaceCatalog catalog, MarketplaceSourceKind source)
            => new()
            {
                SchemaVersion = catalog.SchemaVersion,
                Source = source,
                GeneratedAt = catalog.GeneratedAt,
                Entries = catalog.Entries.Select(entry => new MarketplaceEntry
                {
                    Source = source,
                    Id = entry.Id,
                    Name = entry.Name,
                    Summary = entry.Summary,
                    Description = entry.Description,
                    Publisher = entry.Publisher,
                    Category = entry.Category,
                    Tags = entry.Tags,
                    Versions = entry.Versions,
                }).ToArray(),
            };
    }

    public sealed class LocalMarketplaceRepository : IMarketplaceRepository
    {
        private readonly string _catalogPath;
        private readonly MarketplaceSourceKind _source;
        public LocalMarketplaceRepository(
            string catalogPath,
            MarketplaceSourceKind source = MarketplaceSourceKind.LocalPackage)
        {
            _catalogPath = Path.GetFullPath(catalogPath);
            _source = source;
        }

        public async Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_catalogPath))
                return MarketplaceCatalogResult.Fail("市场目录不存在，本地插件不受影响。");
            await using var stream = File.OpenRead(_catalogPath);
            return await MarketplaceCatalogCodec.ReadAsync(
                stream, cancellationToken, _source);
        }

        public async Task<IReadOnlyList<MarketplaceEntry>> SearchAsync(
            string? query = null, string? category = null,
            CancellationToken cancellationToken = default)
        {
            var result = await LoadAsync(cancellationToken);
            return result.IsSuccess
                ? MarketplaceCatalogCodec.Search(result.Catalog!.Entries, query, category)
                : Array.Empty<MarketplaceEntry>();
        }

        public static MarketplaceInstallState GetInstallState(
            MarketplaceEntry entry, string? installedVersion)
        {
            if (string.IsNullOrWhiteSpace(installedVersion))
                return MarketplaceInstallState.NotInstalled;
            var latest = entry.Versions.Select(x => ParseVersion(x.Version)).Where(x => x != null).Max();
            var installed = ParseVersion(installedVersion);
            if (latest == null || installed == null) return MarketplaceInstallState.Incompatible;
            var comparison = installed.CompareTo(latest);
            return comparison < 0 ? MarketplaceInstallState.UpdateAvailable
                : comparison > 0 ? MarketplaceInstallState.DowngradeAvailable
                : MarketplaceInstallState.Installed;
        }

        private static Version? ParseVersion(string value)
            => Version.TryParse(value.TrimStart('v', 'V'), out var parsed) ? parsed : null;
    }

    public sealed class RemoteMarketplaceRepository : IMarketplaceRepository
    {
        private readonly HttpClient _client;
        private readonly Uri _registryUri;
        private readonly string _cachePath;
        private readonly TimeSpan _timeout;

        public RemoteMarketplaceRepository(
            HttpClient client, Uri registryUri, string cachePath, TimeSpan? timeout = null)
        {
            if (!IsSecureHttp(registryUri))
                throw new ArgumentException("远程 Registry 必须使用 HTTPS。", nameof(registryUri));
            _client = client;
            _registryUri = registryUri;
            _cachePath = Path.GetFullPath(cachePath);
            _timeout = timeout ?? TimeSpan.FromSeconds(8);
        }

        public async Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                using var response = await _client.GetAsync(
                    _registryUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                var finalUri = response.RequestMessage?.RequestUri ?? _registryUri;
                if (!IsSecureHttp(finalUri))
                    return await LoadCacheOrFailAsync("Registry 重定向到了非 HTTPS 地址。", cancellationToken);
                if (response.Content.Headers.ContentLength > MarketplaceCatalogCodec.MaximumCatalogBytes)
                    return await LoadCacheOrFailAsync("远程市场目录超过大小限制。", cancellationToken);

                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var bounded = new MemoryStream();
                await CopyBoundedAsync(source, bounded, MarketplaceCatalogCodec.MaximumCatalogBytes, timeout.Token);
                bounded.Position = 0;
                var parsed = await MarketplaceCatalogCodec.ReadAsync(
                    bounded, timeout.Token, MarketplaceSourceKind.RemoteRegistry);
                if (!parsed.IsSuccess)
                    return await LoadCacheOrFailAsync(parsed.Error!, cancellationToken);
                await WriteCacheAtomicallyAsync(bounded.ToArray(), cancellationToken);
                return parsed;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                return await LoadCacheOrFailAsync($"远程市场不可用：{ex.Message}", cancellationToken);
            }
        }

        public async Task<IReadOnlyList<MarketplaceEntry>> SearchAsync(
            string? query = null, string? category = null,
            CancellationToken cancellationToken = default)
        {
            var result = await LoadAsync(cancellationToken);
            return result.IsSuccess
                ? MarketplaceCatalogCodec.Search(result.Catalog!.Entries, query, category)
                : Array.Empty<MarketplaceEntry>();
        }

        private async Task<MarketplaceCatalogResult> LoadCacheOrFailAsync(
            string networkError, CancellationToken cancellationToken)
        {
            if (!File.Exists(_cachePath)) return MarketplaceCatalogResult.Fail(networkError);
            try
            {
                await using var stream = File.OpenRead(_cachePath);
                var cached = await MarketplaceCatalogCodec.ReadAsync(
                    stream, cancellationToken, MarketplaceSourceKind.RemoteRegistry);
                return cached.IsSuccess
                    ? MarketplaceCatalogResult.Ok(cached.Catalog!, true, $"{networkError} 已使用上次可信目录。")
                    : MarketplaceCatalogResult.Fail(networkError);
            }
            catch (IOException) { return MarketplaceCatalogResult.Fail(networkError); }
        }

        private async Task WriteCacheAtomicallyAsync(byte[] data, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temp = _cachePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, data, cancellationToken);
                File.Move(temp, _cachePath, true);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        private static async Task CopyBoundedAsync(
            Stream source, Stream destination, long maximum, CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximum) throw new IOException("远程市场目录超过大小限制。");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        private static bool IsSecureHttp(Uri uri)
            => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps;
    }

    public sealed class CompositeMarketplaceRepository : IMarketplaceRepository
    {
        private readonly IReadOnlyList<IMarketplaceRepository> _repositories;
        public CompositeMarketplaceRepository(params IMarketplaceRepository[] repositories)
            => _repositories = repositories;

        public async Task<MarketplaceCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            var entries = new Dictionary<string, MarketplaceEntry>(StringComparer.OrdinalIgnoreCase);
            var statuses = new List<string>();
            var anyRemote = false;
            var generated = DateTimeOffset.MinValue;
            foreach (var repository in _repositories)
            {
                var result = await repository.LoadAsync(cancellationToken);
                if (!result.IsSuccess)
                {
                    statuses.Add(result.Error ?? "目录不可用");
                    continue;
                }
                anyRemote |= result.Catalog!.Source == MarketplaceSourceKind.RemoteRegistry;
                generated = generated > result.Catalog.GeneratedAt ? generated : result.Catalog.GeneratedAt;
                foreach (var entry in result.Catalog.Entries) entries[entry.Id] = entry;
                if (!string.IsNullOrWhiteSpace(result.Status)) statuses.Add(result.Status);
            }
            if (entries.Count == 0) return MarketplaceCatalogResult.Fail(string.Join(" ", statuses));
            return MarketplaceCatalogResult.Ok(new MarketplaceCatalog
            {
                SchemaVersion = 1,
                Source = anyRemote ? MarketplaceSourceKind.RemoteRegistry : MarketplaceSourceKind.LocalPackage,
                GeneratedAt = generated,
                Entries = entries.Values.OrderBy(x => x.Name).ToArray(),
            }, statuses.Count > 0, string.Join(" ", statuses));
        }

        public async Task<IReadOnlyList<MarketplaceEntry>> SearchAsync(
            string? query = null, string? category = null,
            CancellationToken cancellationToken = default)
        {
            var result = await LoadAsync(cancellationToken);
            return result.IsSuccess
                ? MarketplaceCatalogCodec.Search(result.Catalog!.Entries, query, category)
                : Array.Empty<MarketplaceEntry>();
        }
    }

    public sealed class MarketplaceCatalogResult
    {
        public bool IsSuccess { get; init; }
        public MarketplaceCatalog? Catalog { get; init; }
        public string? Error { get; init; }
        public bool IsFallback { get; init; }
        public string? Status { get; init; }

        public static MarketplaceCatalogResult Ok(
            MarketplaceCatalog catalog, bool fallback = false, string? status = null)
            => new() { IsSuccess = true, Catalog = catalog, IsFallback = fallback, Status = status };
        public static MarketplaceCatalogResult Fail(string error)
            => new() { Error = error };
    }
}
