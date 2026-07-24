using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public sealed class MarketplacePackageDownloader
    {
        public const long MaximumPackageBytes = 256L * 1024 * 1024;
        private readonly HttpClient _client;
        private readonly string _cacheDirectory;
        private readonly HashSet<string> _allowedHosts;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _retryDelay;
        private readonly int _maximumAttempts;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadGates = new(
            StringComparer.OrdinalIgnoreCase);

        public MarketplacePackageDownloader(
            HttpClient client,
            string cacheDirectory,
            IEnumerable<string> allowedHosts,
            TimeSpan? timeout = null,
            int maximumAttempts = 3,
            TimeSpan? retryDelay = null)
        {
            _client = client;
            _cacheDirectory = Path.GetFullPath(cacheDirectory);
            _allowedHosts = new HashSet<string>(
                allowedHosts.Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            _timeout = timeout ?? TimeSpan.FromSeconds(60);
            _maximumAttempts = Math.Clamp(maximumAttempts, 1, 5);
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
        }

        public async Task<PackageDownloadResult> DownloadAsync(
            string pluginId,
            MarketplacePackageVersion version,
            CancellationToken cancellationToken = default)
        {
            var packageUri = version.PackageUri;
            if (packageUri == null || !IsAllowed(packageUri))
                return PackageDownloadResult.Fail(
                    MarketplaceErrorCode.DownloadUriNotAllowed,
                    "插件包地址必须是已允许主机上的 HTTPS 地址。");
            if (!IsSha256(version.Sha256))
                return PackageDownloadResult.Fail(
                    MarketplaceErrorCode.DownloadHashMissing,
                    "远程插件缺少有效 SHA-256。");

            Directory.CreateDirectory(_cacheDirectory);
            CleanupStaleTemporaryFiles();
            var finalPath = Path.Combine(
                _cacheDirectory,
                $"{Sanitize(pluginId)}-{Sanitize(version.Version)}-{version.Sha256[..12]}.lpak");
            var downloadGate = _downloadGates.GetOrAdd(finalPath, _ => new SemaphoreSlim(1, 1));
            await downloadGate.WaitAsync(cancellationToken);
            try
            {
                return await DownloadCoreAsync(finalPath, packageUri, version, cancellationToken);
            }
            finally { downloadGate.Release(); }
        }

        private async Task<PackageDownloadResult> DownloadCoreAsync(
            string finalPath,
            Uri packageUri,
            MarketplacePackageVersion version,
            CancellationToken cancellationToken)
        {
            if (File.Exists(finalPath))
            {
                var cachedHash = await ComputeHashAsync(finalPath, cancellationToken);
                if (HashesEqual(cachedHash, version.Sha256))
                    return PackageDownloadResult.Ok(
                        finalPath, true, new FileInfo(finalPath).Length, 0);
                try { File.Delete(finalPath); }
                catch (Exception ex)
                {
                    return PackageDownloadResult.Fail(
                        MarketplaceErrorCode.DownloadCacheFailure,
                        ex.Message);
                }
            }

            for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
            {
                var tempPath = Path.Combine(_cacheDirectory, $".download-{Guid.NewGuid():N}.tmp");
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_timeout);
                    using var response = await _client.GetAsync(
                        packageUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    var finalUri = response.RequestMessage?.RequestUri ?? packageUri;
                    if (!IsAllowed(finalUri))
                        return PackageDownloadResult.Fail(
                            MarketplaceErrorCode.DownloadRedirectNotAllowed,
                            "插件包重定向到了未允许的地址。",
                            attempt);
                    if (response.Content.Headers.ContentLength > MaximumPackageBytes)
                        return PackageDownloadResult.Fail(
                            MarketplaceErrorCode.DownloadTooLarge,
                            "插件包超过 256 MB 安全限制。",
                            attempt);

                    await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                    await using var destination = new FileStream(
                        tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                    {
                        total += read;
                        if (total > MaximumPackageBytes)
                            return PackageDownloadResult.Fail(
                                MarketplaceErrorCode.DownloadTooLarge,
                                "插件包超过 256 MB 安全限制。",
                                attempt);
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    }
                    await destination.FlushAsync(timeout.Token);
                    var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                    if (!HashesEqual(actualHash, version.Sha256))
                        return PackageDownloadResult.Fail(
                            MarketplaceErrorCode.DownloadHashMismatch,
                            "下载包 SHA-256 与 Registry 不一致。",
                            attempt);
                    destination.Close();
                    File.Move(tempPath, finalPath, true);
                    return PackageDownloadResult.Ok(finalPath, false, total, attempt);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return PackageDownloadResult.Fail(
                        MarketplaceErrorCode.DownloadCanceled,
                        "下载已取消。",
                        attempt);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    if (attempt >= _maximumAttempts || !ShouldRetry(ex))
                    {
                        var message = ex is OperationCanceledException
                            ? "插件包下载超时。"
                            : $"插件包下载失败：{ex.Message}";
                        return PackageDownloadResult.Fail(
                            ex is OperationCanceledException
                                ? MarketplaceErrorCode.DownloadTimeout
                                : MarketplaceErrorCode.DownloadFailed,
                            message,
                            attempt);
                    }
                    await Task.Delay(_retryDelay, cancellationToken);
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }

            return PackageDownloadResult.Fail(
                MarketplaceErrorCode.DownloadFailed,
                "插件包下载失败。",
                _maximumAttempts);
        }

        private static bool ShouldRetry(Exception exception)
            => exception switch
            {
                OperationCanceledException => true,
                IOException => true,
                HttpRequestException { StatusCode: null } => true,
                HttpRequestException { StatusCode: var status } =>
                    (int)status >= 500 || (int)status is 408 or 429,
                _ => false,
            };

        private void CleanupStaleTemporaryFiles()
        {
            var cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromHours(1));
            foreach (var path in Directory.GetFiles(_cacheDirectory, ".download-*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch { }
            }
        }

        private bool IsAllowed(Uri uri)
            => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
                && _allowedHosts.Contains(uri.IdnHost);

        private static bool IsSha256(string value)
            => value.Length == 64 && value.All(Uri.IsHexDigit);

        private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        private static bool HashesEqual(string left, string right)
        {
            try
            {
                return CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(left), Convert.FromHexString(right));
            }
            catch { return false; }
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) || c is '.' or '/' or '\\' ? '-' : c).ToArray());
        }
    }

    public sealed class PackageDownloadResult
    {
        public bool IsSuccess { get; init; }
        public string? PackagePath { get; init; }
        public string? Error { get; init; }
        public MarketplaceErrorCode ErrorCode { get; init; }
        public bool FromCache { get; init; }
        public long Bytes { get; init; }
        public int Attempts { get; init; }
        public static PackageDownloadResult Ok(string path, bool cache, long bytes, int attempts = 0)
            => new()
            {
                IsSuccess = true,
                PackagePath = path,
                ErrorCode = MarketplaceErrorCode.None,
                FromCache = cache,
                Bytes = bytes,
                Attempts = attempts,
            };
        public static PackageDownloadResult Fail(
            MarketplaceErrorCode code,
            string technicalMessage,
            int attempts = 0)
            => new()
            {
                ErrorCode = code,
                Error = technicalMessage,
                Attempts = attempts,
            };
    }

    public static class MarketplaceConfigurationLoader
    {
        public static async Task<MarketplaceSettings> LoadSettingsAsync(
            string path, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(path)) return new MarketplaceSettings();
            try
            {
                await using var stream = File.OpenRead(path);
                var settings = await JsonSerializer.DeserializeAsync<MarketplaceSettings>(
                    stream, MarketplaceCatalogCodec.JsonOptions, cancellationToken);
                if (settings?.SchemaVersion != 1) return new MarketplaceSettings();
                return new MarketplaceSettings
                {
                    SchemaVersion = 1,
                    RegistryUri = settings.RegistryUri,
                    CatalogTimeoutSeconds = Math.Clamp(settings.CatalogTimeoutSeconds, 2, 30),
                    DownloadTimeoutSeconds = Math.Clamp(settings.DownloadTimeoutSeconds, 10, 300),
                    AllowedPackageHosts = settings.AllowedPackageHosts,
                };
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return new MarketplaceSettings();
            }
        }

        public static async Task<PublisherTrustStoreLoadResult> LoadTrustStoreAsync(
            string path, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(path))
                return PublisherTrustStoreLoadResult.Fail("发布者根信任文件不存在。");
            try
            {
                await using var stream = File.OpenRead(path);
                var document = await JsonSerializer.DeserializeAsync<TrustedPublisherDocument>(
                    stream, MarketplaceCatalogCodec.JsonOptions, cancellationToken);
                if (document?.SchemaVersion != 1)
                    return PublisherTrustStoreLoadResult.Fail("发布者根信任文件版本不受支持。");
                var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var publisher in document.Publishers)
                {
                    if (string.IsNullOrWhiteSpace(publisher.KeyId)
                        || !string.Equals(publisher.Algorithm, "RSA-SHA256", StringComparison.OrdinalIgnoreCase)
                        || keys.ContainsKey(publisher.KeyId))
                        return PublisherTrustStoreLoadResult.Fail("发布者根信任条目无效或 Key ID 重复。");
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(publisher.PublicKeyPem);
                    if (rsa.KeySize < 2048)
                        return PublisherTrustStoreLoadResult.Fail($"发布者密钥 {publisher.KeyId} 小于 2048 位。");
                    var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
                    if (!string.IsNullOrWhiteSpace(publisher.Sha256Fingerprint)
                        && !string.Equals(
                            fingerprint, publisher.Sha256Fingerprint.Replace(":", string.Empty),
                            StringComparison.OrdinalIgnoreCase))
                        return PublisherTrustStoreLoadResult.Fail($"发布者密钥 {publisher.KeyId} 指纹不匹配。");
                    keys.Add(publisher.KeyId, publisher.PublicKeyPem);
                }
                return PublisherTrustStoreLoadResult.Ok(new PublisherTrustStore(keys), keys.Count);
            }
            catch (Exception ex) when (ex is IOException or JsonException or CryptographicException
                or ArgumentException or FormatException)
            {
                return PublisherTrustStoreLoadResult.Fail($"发布者根信任文件无效：{ex.Message}");
            }
        }
    }

    public sealed class PublisherTrustStoreLoadResult
    {
        public bool IsSuccess { get; init; }
        public PublisherTrustStore Store { get; init; } = PublisherTrustStore.Empty;
        public int KeyCount { get; init; }
        public string? Error { get; init; }
        public static PublisherTrustStoreLoadResult Ok(PublisherTrustStore store, int count)
            => new() { IsSuccess = true, Store = store, KeyCount = count };
        public static PublisherTrustStoreLoadResult Fail(string error) => new() { Error = error };
    }
}
