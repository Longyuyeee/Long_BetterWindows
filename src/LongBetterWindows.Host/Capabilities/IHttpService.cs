using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    /// <summary>
    /// HTTP 请求能力——插件可通过此接口发起网络请求。
    /// 需要 manifest.json 中声明 "network.http" 能力。
    /// </summary>
    public interface IHttpService
    {
        Task<HostApiResponse<string>> GetAsync(string url, Dictionary<string, string>? headers = null);
        Task<HostApiResponse<string>> PostAsync(string url, string body, string contentType = "application/json", Dictionary<string, string>? headers = null);
        Task<HostApiResponse<byte[]>> DownloadAsync(string url);
    }
}
