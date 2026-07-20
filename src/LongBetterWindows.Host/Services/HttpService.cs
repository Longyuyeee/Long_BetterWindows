using System.Net.Http;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class HttpService : IHttpService, IDisposable
    {
        private readonly HttpClient _client;
        private const long MaxResponseSize = 10 * 1024 * 1024; // 10MB

        // ✅ SSRF 防护：阻止访问内网地址
        private static readonly HashSet<string> BlockedHostPrefixes = new()
        {
            "localhost", "127.0.0.1", "0.0.0.0",
            "10.", "172.16.", "172.17.", "172.18.", "172.19.",
            "172.20.", "172.21.", "172.22.", "172.23.", "172.24.",
            "172.25.", "172.26.", "172.27.", "172.28.", "172.29.",
            "172.30.", "172.31.",
            "192.168.", "169.254."
        };

        public HttpService()
        {
            _client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _client.DefaultRequestHeaders.Add("User-Agent", $"LongBetterWindows/{App.ProductVersion}");
        }

        public async Task<HostApiResponse<string>> GetAsync(string url, Dictionary<string, string>? headers = null)
        {
            try
            {
                // ✅ 验证 URL 并阻止内网访问
                var validationResult = ValidateUrl(url);
                if (!validationResult.IsValid)
                    return HostApiResponse<string>.Failure(ApiErrorCode.InvalidArgument, validationResult.Error!);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyHeaders(request, headers);

                var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                // ✅ 检查响应大小
                if (response.Content.Headers.ContentLength > MaxResponseSize)
                {
                    Log.Warning("HTTP 响应超过大小限制: {Url} ({Size} bytes)", url, response.Content.Headers.ContentLength);
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.Unknown,
                        $"响应内容过大（超过 {MaxResponseSize / 1024 / 1024}MB 限制）");
                }

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();

                Log.Debug("HTTP GET {Url} → {StatusCode}", url, (int)response.StatusCode);
                return HostApiResponse<string>.Success(body);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTTP GET 失败: {Url}", url);
                return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<string>> PostAsync(string url, string body, string contentType = "application/json", Dictionary<string, string>? headers = null)
        {
            try
            {
                // ✅ 验证 URL 并阻止内网访问
                var validationResult = ValidateUrl(url);
                if (!validationResult.IsValid)
                    return HostApiResponse<string>.Failure(ApiErrorCode.InvalidArgument, validationResult.Error!);

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
                ApplyHeaders(request, headers);

                var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                // ✅ 检查响应大小
                if (response.Content.Headers.ContentLength > MaxResponseSize)
                {
                    Log.Warning("HTTP 响应超过大小限制: {Url} ({Size} bytes)", url, response.Content.Headers.ContentLength);
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.Unknown,
                        $"响应内容过大（超过 {MaxResponseSize / 1024 / 1024}MB 限制）");
                }

                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                Log.Debug("HTTP POST {Url} → {StatusCode}", url, (int)response.StatusCode);
                return HostApiResponse<string>.Success(responseBody);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTTP POST 失败: {Url}", url);
                return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        public async Task<HostApiResponse<byte[]>> DownloadAsync(string url)
        {
            try
            {
                // ✅ 验证 URL 并阻止内网访问
                var validationResult = ValidateUrl(url);
                if (!validationResult.IsValid)
                    return HostApiResponse<byte[]>.Failure(ApiErrorCode.InvalidArgument, validationResult.Error!);

                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                // ✅ 检查下载大小
                if (response.Content.Headers.ContentLength > MaxResponseSize)
                {
                    Log.Warning("下载文件超过大小限制: {Url} ({Size} bytes)", url, response.Content.Headers.ContentLength);
                    return HostApiResponse<byte[]>.Failure(
                        ApiErrorCode.Unknown,
                        $"下载内容过大（超过 {MaxResponseSize / 1024 / 1024}MB 限制）");
                }

                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync();

                Log.Debug("HTTP 下载 {Url} → {Bytes} 字节", url, data.Length);
                return HostApiResponse<byte[]>.Success(data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTTP 下载失败: {Url}", url);
                return HostApiResponse<byte[]>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        /// <summary>
        /// 验证 URL 安全性，防止 SSRF 攻击
        /// </summary>
        private static (bool IsValid, string? Error) ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return (false, "URL 不能为空");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return (false, "无效的 URL 格式");

            // 只允许 HTTP/HTTPS
            if (uri.Scheme != "http" && uri.Scheme != "https")
                return (false, $"不支持的协议: {uri.Scheme}");

            // 阻止访问内网地址
            var host = uri.Host.ToLowerInvariant();
            if (BlockedHostPrefixes.Any(prefix => host.StartsWith(prefix)))
            {
                Log.Warning("阻止访问内网地址: {Host}", host);
                return (false, "不允许访问内网地址");
            }

            return (true, null);
        }

        private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
        {
            if (headers == null) return;
            foreach (var kv in headers)
            {
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
