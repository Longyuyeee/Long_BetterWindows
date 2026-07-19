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
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyHeaders(request, headers);

                var response = await _client.SendAsync(request);
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
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
                ApplyHeaders(request, headers);

                var response = await _client.SendAsync(request);
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
                var data = await _client.GetByteArrayAsync(url);
                Log.Debug("HTTP 下载 {Url} → {Bytes} 字节", url, data.Length);
                return HostApiResponse<byte[]>.Success(data);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTTP 下载失败: {Url}", url);
                return HostApiResponse<byte[]>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
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
