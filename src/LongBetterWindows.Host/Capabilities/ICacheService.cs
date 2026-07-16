using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface ICacheService
    {
        /// <summary>清理系统临时文件</summary>
        Task<HostApiResponse<CleanupResult>> CleanTempFilesAsync();

        /// <summary>清理 Windows 更新缓存</summary>
        Task<HostApiResponse<CleanupResult>> CleanWindowsUpdateCacheAsync();

        /// <summary>清理浏览器缓存</summary>
        Task<HostApiResponse<CleanupResult>> CleanBrowserCacheAsync(string browser);

        /// <summary>清理回收站</summary>
        Task<HostApiResponse<CleanupResult>> EmptyRecycleBinAsync();

        /// <summary>获取缓存文件统计</summary>
        Task<HostApiResponse<CacheStatistics>> GetCacheStatisticsAsync();

        /// <summary>批量清理（一键清理）</summary>
        Task<HostApiResponse<CleanupSummary>> CleanAllAsync();
    }

    public class CleanupResult
    {
        public int FilesDeleted { get; set; }
        public long SpaceFreed { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class CacheStatistics
    {
        public long TempFilesSize { get; set; }
        public long WindowsUpdateSize { get; set; }
        public long BrowserCacheSize { get; set; }
        public long RecycleBinSize { get; set; }
        public long TotalSize { get; set; }
    }

    public class CleanupSummary
    {
        public int TotalFilesDeleted { get; set; }
        public long TotalSpaceFreed { get; set; }
        public Dictionary<string, CleanupResult> Details { get; set; } = new();
    }
}
