using System.IO;
using System.Runtime.InteropServices;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class CacheService : ICacheService
    {
        public Task<HostApiResponse<CleanupResult>> CleanTempFilesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var result = new CleanupResult();
                    var tempPaths = new[]
                    {
                        Path.GetTempPath(),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
                    };

                    foreach (var tempPath in tempPaths)
                    {
                        if (!Directory.Exists(tempPath)) continue;

                        var files = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                var size = fi.Length;
                                File.Delete(file);
                                result.FilesDeleted++;
                                result.SpaceFreed += size;
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add($"{file}: {ex.Message}");
                            }
                        }
                    }

                    return HostApiResponse<CleanupResult>.Success(result);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<CleanupResult>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<CleanupResult>> CleanWindowsUpdateCacheAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var result = new CleanupResult();
                    var updateCachePath = @"C:\Windows\SoftwareDistribution\Download";

                    if (!Directory.Exists(updateCachePath))
                        return HostApiResponse<CleanupResult>.Success(result);

                    var files = Directory.GetFiles(updateCachePath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            var size = fi.Length;
                            File.Delete(file);
                            result.FilesDeleted++;
                            result.SpaceFreed += size;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"{file}: {ex.Message}");
                        }
                    }

                    return HostApiResponse<CleanupResult>.Success(result);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<CleanupResult>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<CleanupResult>> CleanBrowserCacheAsync(string browser)
        {
            return Task.Run(() =>
            {
                try
                {
                    var result = new CleanupResult();
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    var cachePaths = browser.ToLower() switch
                    {
                        "chrome" => new[] { Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache") },
                        "edge" => new[] { Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache") },
                        "firefox" => new[] { Path.Combine(localAppData, @"Mozilla\Firefox\Profiles") },
                        _ => Array.Empty<string>()
                    };

                    foreach (var cachePath in cachePaths)
                    {
                        if (!Directory.Exists(cachePath)) continue;

                        var files = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                var size = fi.Length;
                                File.Delete(file);
                                result.FilesDeleted++;
                                result.SpaceFreed += size;
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add($"{file}: {ex.Message}");
                            }
                        }
                    }

                    return HostApiResponse<CleanupResult>.Success(result);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<CleanupResult>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<CleanupResult>> EmptyRecycleBinAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var result = new CleanupResult();
                    var hresult = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                    if (hresult == 0)
                    {
                        result.FilesDeleted = -1; // 无法准确计数
                        result.SpaceFreed = -1;
                    }

                    return HostApiResponse<CleanupResult>.Success(result);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<CleanupResult>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<CacheStatistics>> GetCacheStatisticsAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var stats = new CacheStatistics();

                    // 临时文件
                    var tempPath = Path.GetTempPath();
                    if (Directory.Exists(tempPath))
                    {
                        stats.TempFilesSize = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
                    }

                    // 浏览器缓存（Chrome 示例）
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var chromeCachePath = Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache");
                    if (Directory.Exists(chromeCachePath))
                    {
                        stats.BrowserCacheSize = Directory.GetFiles(chromeCachePath, "*.*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
                    }

                    stats.TotalSize = stats.TempFilesSize + stats.WindowsUpdateSize + stats.BrowserCacheSize + stats.RecycleBinSize;

                    return HostApiResponse<CacheStatistics>.Success(stats);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<CacheStatistics>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse<CleanupSummary>> CleanAllAsync()
        {
            try
            {
                var summary = new CleanupSummary();

                var tempResult = await CleanTempFilesAsync();
                if (tempResult.IsSuccess && tempResult.Data != null)
                {
                    summary.Details["temp"] = tempResult.Data;
                    summary.TotalFilesDeleted += tempResult.Data.FilesDeleted;
                    summary.TotalSpaceFreed += tempResult.Data.SpaceFreed;
                }

                var recycleBinResult = await EmptyRecycleBinAsync();
                if (recycleBinResult.IsSuccess && recycleBinResult.Data != null)
                {
                    summary.Details["recyclebin"] = recycleBinResult.Data;
                }

                return HostApiResponse<CleanupSummary>.Success(summary);
            }
            catch (Exception ex)
            {
                return HostApiResponse<CleanupSummary>.Failure(ApiErrorCode.Unknown, ex.Message);
            }
        }

        #region Win32 API

        [DllImport("shell32.dll")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        #endregion
    }
}
