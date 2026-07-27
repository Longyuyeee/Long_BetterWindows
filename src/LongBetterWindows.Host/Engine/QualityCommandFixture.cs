using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 仅供显式质量命令使用的进程级 Web 能力夹具。
    /// 每个质量案例运行在独立宿主进程中，避免访问用户剪贴板、存储或网络。
    /// </summary>
    internal sealed class QualityCommandFixture : IDisposable
    {
        private static readonly object CurrentLock = new();
        private static QualityCommandFixture? _current;

        private readonly object _stateLock = new();
        private readonly QualityCommandFixtureDefinition _definition;
        private readonly Dictionary<string, string> _storage;
        private readonly Dictionary<string, int> _calls =
            new(StringComparer.Ordinal);
        private string? _clipboardText;
        private int _monitoringLeaseCount;
        private int _disposed;
        private string? _lastHttpUrlSha256;

        private QualityCommandFixture(QualityCommandFixtureDefinition definition)
        {
            _definition = definition;
            _clipboardText = definition.Clipboard?.Text;
            _storage = new Dictionary<string, string>(
                definition.Storage?.Values
                    ?? new Dictionary<string, string>(),
                StringComparer.Ordinal);
        }

        internal static QualityCommandFixture? Current
        {
            get
            {
                lock (CurrentLock)
                    return _current;
            }
        }

        internal static IDisposable Install(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var definition = JsonSerializer.Deserialize<QualityCommandFixtureDefinition>(
                File.ReadAllText(fullPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }) ?? throw new InvalidDataException(
                    "Quality command fixture is empty.");
            if (definition.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported quality command fixture schema: {definition.SchemaVersion}.");
            }

            lock (CurrentLock)
            {
                if (_current is not null)
                    throw new InvalidOperationException(
                        "A quality command fixture is already installed.");
                _current = new QualityCommandFixture(definition);
                return _current;
            }
        }

        internal bool TryDispatch(
            string method,
            object?[] args,
            out Task<object?> response)
        {
            lock (_stateLock)
            {
                ThrowIfDisposed();
                if (!IsFixtureMethod(method))
                {
                    response = Task.FromResult<object?>(null);
                    return false;
                }

                _calls[method] = _calls.TryGetValue(method, out var count)
                    ? count + 1
                    : 1;
                response = Task.FromResult<object?>(DispatchCore(method, args));
                return true;
            }
        }

        internal QualityCommandFixtureSnapshot CreateSnapshot()
        {
            lock (_stateLock)
            {
                ThrowIfDisposed();
                return new QualityCommandFixtureSnapshot(
                    _clipboardText,
                    _monitoringLeaseCount,
                    new Dictionary<string, string>(_storage, StringComparer.Ordinal),
                    new Dictionary<string, int>(_calls, StringComparer.Ordinal),
                    _lastHttpUrlSha256);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            lock (CurrentLock)
            {
                if (ReferenceEquals(_current, this))
                    _current = null;
            }
        }

        private object DispatchCore(string method, object?[] args)
            => method switch
            {
                "clipboard.getText" => ErrorOrData(
                    _definition.Clipboard?.ReadError,
                    _clipboardText),
                "clipboard.setText" => SetClipboard(
                    WebPluginArguments.GetString(args, 0)),
                "clipboard.clear" => SetClipboard(null),
                "clipboard.startMonitoring" => StartMonitoring(),
                "clipboard.stopMonitoring" => StopMonitoring(),
                "storage.get" => GetStorage(WebPluginArguments.GetString(args, 0)),
                "storage.set" => SetStorage(
                    WebPluginArguments.GetString(args, 0),
                    WebPluginArguments.GetString(args, 1)),
                "storage.compareExchange" => CompareExchangeStorage(
                    WebPluginArguments.GetString(args, 0),
                    WebPluginArguments.GetNullableString(args, 1),
                    WebPluginArguments.GetString(args, 2)),
                "storage.delete" => DeleteStorage(
                    WebPluginArguments.GetString(args, 0)),
                "storage.containsKey" => ContainsStorage(
                    WebPluginArguments.GetString(args, 0)),
                "http.get" => GetHttp(WebPluginArguments.GetString(args, 0)),
                "http.post" or "http.download" => new
                {
                    success = false,
                    error = "Quality fixture does not allow HTTP mutation or download.",
                },
                _ => new { success = false, error = $"Unsupported fixture method: {method}" },
            };

        private object SetClipboard(string? value)
        {
            var error = _definition.Clipboard?.WriteError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, error };
            _clipboardText = value;
            return new { success = true };
        }

        private object StartMonitoring()
        {
            var error = _definition.Clipboard?.MonitorError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, error };
            _monitoringLeaseCount++;
            return new { success = true };
        }

        private object StopMonitoring()
        {
            if (_monitoringLeaseCount > 0)
                _monitoringLeaseCount--;
            return new { success = true };
        }

        private object GetStorage(string key)
        {
            var error = _definition.Storage?.ReadError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, data = (string?)null, error };
            _storage.TryGetValue(key, out var value);
            return new { success = true, data = value, error = (string?)null };
        }

        private object SetStorage(string key, string value)
        {
            var error = _definition.Storage?.WriteError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, error };
            _storage[key] = value;
            return new { success = true };
        }

        private object CompareExchangeStorage(
            string key,
            string? expected,
            string value)
        {
            var error = _definition.Storage?.WriteError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, data = false, error };
            _storage.TryGetValue(key, out var current);
            if (!string.Equals(current, expected, StringComparison.Ordinal))
                return new { success = true, data = false, error = (string?)null };
            _storage[key] = value;
            return new { success = true, data = true, error = (string?)null };
        }

        private object DeleteStorage(string key)
        {
            var error = _definition.Storage?.WriteError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, error };
            _storage.Remove(key);
            return new { success = true };
        }

        private object ContainsStorage(string key)
        {
            var error = _definition.Storage?.ReadError;
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, data = false, error = (string?)error };
            return new
            {
                success = true,
                data = _storage.ContainsKey(key),
                error = (string?)null,
            };
        }

        private object GetHttp(string url)
        {
            _lastHttpUrlSha256 = Sha256(url);
            var fixture = _definition.Http;
            if (fixture is null)
            {
                return new
                {
                    success = false,
                    data = (string?)null,
                    error = "No quality HTTP response was configured.",
                };
            }
            if (!string.IsNullOrWhiteSpace(fixture.RequiredUrlContains)
                && !url.Contains(
                    fixture.RequiredUrlContains,
                    StringComparison.Ordinal))
            {
                return new
                {
                    success = false,
                    data = (string?)null,
                    error = "Quality HTTP URL did not match the fixture.",
                };
            }
            return ErrorOrData(fixture.Error, fixture.Data);
        }

        private static object ErrorOrData(string? error, string? data)
        {
            if (!string.IsNullOrWhiteSpace(error))
                return new { success = false, data = (string?)null, error = (string?)error };
            return new { success = true, data, error = (string?)null };
        }

        private static bool IsFixtureMethod(string method)
            => method.StartsWith("clipboard.", StringComparison.Ordinal)
                || method.StartsWith("storage.", StringComparison.Ordinal)
                || method.StartsWith("http.", StringComparison.Ordinal);

        private static string Sha256(string value)
            => Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
    }

    internal sealed class QualityCommandFixtureDefinition
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("clipboard")]
        public QualityClipboardFixture? Clipboard { get; init; }

        [JsonPropertyName("storage")]
        public QualityStorageFixture? Storage { get; init; }

        [JsonPropertyName("http")]
        public QualityHttpFixture? Http { get; init; }
    }

    internal sealed class QualityClipboardFixture
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("read_error")]
        public string? ReadError { get; init; }

        [JsonPropertyName("write_error")]
        public string? WriteError { get; init; }

        [JsonPropertyName("monitor_error")]
        public string? MonitorError { get; init; }
    }

    internal sealed class QualityStorageFixture
    {
        [JsonPropertyName("values")]
        public Dictionary<string, string> Values { get; init; } =
            new(StringComparer.Ordinal);

        [JsonPropertyName("read_error")]
        public string? ReadError { get; init; }

        [JsonPropertyName("write_error")]
        public string? WriteError { get; init; }
    }

    internal sealed class QualityHttpFixture
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("required_url_contains")]
        public string? RequiredUrlContains { get; init; }
    }

    internal sealed record QualityCommandFixtureSnapshot(
        [property: JsonPropertyName("clipboard_text")] string? ClipboardText,
        [property: JsonPropertyName("monitoring_lease_count")] int MonitoringLeaseCount,
        [property: JsonPropertyName("storage")] IReadOnlyDictionary<string, string> Storage,
        [property: JsonPropertyName("calls")] IReadOnlyDictionary<string, int> Calls,
        [property: JsonPropertyName("last_http_url_sha256")] string? LastHttpUrlSha256);
}
