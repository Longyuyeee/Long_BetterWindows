using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IStorageService
    {
        Task<HostApiResponse<string?>> GetAsync(string key);
        Task<HostApiResponse> SetAsync(string key, string value);
        Task<HostApiResponse<bool>> CompareExchangeAsync(
            string key,
            string? expectedValue,
            string value)
            => Task.FromResult(HostApiResponse<bool>.Failure(
                ApiErrorCode.NotSupported,
                "当前存储实现不支持原子比较交换。"));
        Task<HostApiResponse> DeleteAsync(string key);
        Task<HostApiResponse<bool>> ContainsKeyAsync(string key);
    }
}
