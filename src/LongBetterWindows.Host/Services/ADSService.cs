using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;
using static LongBetterWindows.Host.Services.NativeMethods;

namespace LongBetterWindows.Host.Services
{
    public class ADSService : IADSService
    {
        private const string DefaultStreamName = "long_note";
        private const string FallbackFileName = "long_note.json";
        private readonly RollbackEngine _rollback;

        public ADSService(RollbackEngine rollback)
        {
            _rollback = rollback;
        }

        public Task<HostApiResponse<string>> ReadAsync(string filePath, string streamName)
        {
            return Task.Run(() =>
            {
                try
                {
                    var adsPath = BuildAdsPath(filePath, streamName);
                    var handle = CreateFileW(
                        adsPath,
                        GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        IsDirectory(filePath) ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();

                        if (error == 2 || error == 3) // ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND
                        {
                            var fallback = TryReadFallback(filePath);
                            if (fallback != null)
                            {
                                return HostApiResponse<string>.Success(fallback);
                            }

                            return HostApiResponse<string>.Failure(
                                ApiErrorCode.StreamNotFound, "备用数据流不存在。");
                        }

                        return HostApiResponse<string>.Failure(
                            ApiErrorCode.Win32Error, $"无法打开文件流 (Win32: {error})。");
                    }

                    try
                    {
                        var content = ReadStreamContent(handle);
                        return HostApiResponse<string>.Success(content);
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 读取失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> WriteAsync(string filePath, string streamName, string content)
        {
            return Task.Run(() =>
            {
                try
                {
                    // 先读取旧内容，供回滚时恢复
                    var oldContent = TryReadExistingContent(filePath, streamName);

                    var adsPath = BuildAdsPath(filePath, streamName);
                    var handle = CreateFileW(
                        adsPath,
                        GENERIC_WRITE,
                        FILE_SHARE_READ,
                        IntPtr.Zero,
                        CREATE_ALWAYS,
                        IsDirectory(filePath) ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();

                        if (error == 1 || error == 50) // ERROR_INVALID_FUNCTION or ERROR_NOT_SUPPORTED
                        {
                            var oldFallback = TryReadFallback(filePath);
                            WriteFallback(filePath, content);
                            Log.Information("非 NTFS 卷，使用回退文件: {Path}", filePath);
                            RecordAdsChange(ChangeAction.AdsWrite, filePath, streamName, oldFallback);
                            return HostApiResponse.Success();
                        }

                        Log.Warning("ADS 写入失败, Win32Error={Error}, 回退到文件方案", error);
                        var oldFb = TryReadFallback(filePath);
                        WriteFallback(filePath, content);
                        RecordAdsChange(ChangeAction.AdsWrite, filePath, streamName, oldFb);
                        return HostApiResponse.Success();
                    }

                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(content);
                        if (!WriteFile(handle, bytes, (uint)bytes.Length, out _, IntPtr.Zero))
                        {
                            int error = Marshal.GetLastWin32Error();
                            return HostApiResponse.Failure(
                                ApiErrorCode.Win32Error, $"写入流失败 (Win32: {error})。");
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }

                    Log.Debug("ADS 写入成功: {Path}:{Stream}, {Bytes} 字节",
                        filePath, streamName, content.Length);

                    RecordAdsChange(ChangeAction.AdsWrite, filePath, streamName, oldContent);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 写入失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> DeleteAsync(string filePath, string streamName)
        {
            return Task.Run(() =>
            {
                try
                {
                    // 先读取现有内容，供回滚时恢复
                    var oldContent = TryReadExistingContent(filePath, streamName);

                    var adsPath = BuildAdsPath(filePath, streamName);

                    if (!DeleteFileW(adsPath))
                    {
                        int error = Marshal.GetLastWin32Error();

                        if (error == 2 || error == 3)
                        {
                            var oldFallback = TryReadFallback(filePath);
                            DeleteFallback(filePath);
                            RecordAdsChange(ChangeAction.AdsDelete, filePath, streamName, oldFallback);
                            return HostApiResponse.Success();
                        }

                        return HostApiResponse.Failure(
                            ApiErrorCode.Win32Error, $"删除流失败 (Win32: {error})。");
                    }

                    DeleteFallback(filePath);
                    Log.Debug("ADS 已删除: {Path}:{Stream}", filePath, streamName);
                    RecordAdsChange(ChangeAction.AdsDelete, filePath, streamName, oldContent);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 删除失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<bool>> ExistsAsync(string filePath, string streamName)
        {
            return Task.Run(() =>
            {
                try
                {
                    var adsPath = BuildAdsPath(filePath, streamName);
                    var handle = CreateFileW(
                        adsPath,
                        GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        IsDirectory(filePath) ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    {
                        bool fallbackExists = HasFallback(filePath);
                        return HostApiResponse<bool>.Success(fallbackExists);
                    }

                    CloseHandle(handle);
                    return HostApiResponse<bool>.Success(true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 存在检查失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse<bool>.Failure(
                        ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<bool>> IsNTFSVolumeAsync(string filePath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var driveRoot = Path.GetPathRoot(filePath);
                    if (string.IsNullOrEmpty(driveRoot))
                    {
                        return HostApiResponse<bool>.Success(false);
                    }

                    var testPath = Path.Combine(driveRoot, "ntfs_test_tmp_stream_check");
                    var adsPath = $"{testPath}:__long_check__";

                    var handle = CreateFileW(
                        adsPath,
                        GENERIC_WRITE,
                        FILE_SHARE_READ,
                        IntPtr.Zero,
                        CREATE_ALWAYS,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    {
                        return HostApiResponse<bool>.Success(false);
                    }

                    CloseHandle(handle);
                    DeleteFileW(adsPath);
                    return HostApiResponse<bool>.Success(true);
                }
                catch
                {
                    return HostApiResponse<bool>.Success(false);
                }
            });
        }

        private static string BuildAdsPath(string filePath, string streamName)
        {
            var name = string.IsNullOrEmpty(streamName) ? DefaultStreamName : streamName;
            return $"{filePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}:{name}";
        }

        private static string ReadStreamContent(IntPtr handle)
        {
            var buffer = new byte[4096];
            using var ms = new MemoryStream();

            while (true)
            {
                if (!ReadFile(handle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero))
                    break;

                if (bytesRead == 0)
                    break;

                ms.Write(buffer, 0, (int)bytesRead);

                if (bytesRead < buffer.Length)
                    break;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static string GetFallbackPath(string filePath)
        {
            var dir = IsDirectory(filePath) ? filePath : Path.GetDirectoryName(filePath);
            return Path.Combine(dir ?? ".", FallbackFileName);
        }

        private static string? TryReadFallback(string filePath)
        {
            var fallbackPath = GetFallbackPath(filePath);
            if (File.Exists(fallbackPath))
            {
                return File.ReadAllText(fallbackPath, Encoding.UTF8);
            }

            return null;
        }

        private static void WriteFallback(string filePath, string content)
        {
            var fallbackPath = GetFallbackPath(filePath);
            File.WriteAllText(fallbackPath, content, Encoding.UTF8);
        }

        private static void DeleteFallback(string filePath)
        {
            var fallbackPath = GetFallbackPath(filePath);
            if (File.Exists(fallbackPath))
            {
                File.Delete(fallbackPath);
            }
        }

        private static bool HasFallback(string filePath)
        {
            return File.Exists(GetFallbackPath(filePath));
        }

        private static bool IsDirectory(string path)
        {
            if (Directory.Exists(path))
                return true;

            var ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext);
        }

        private void RecordAdsChange(ChangeAction action, string filePath, string streamName, string? oldValue = null)
        {
            var pluginId = PluginAccessContext.CurrentPluginId ?? "builtin";
            _rollback.RecordChange(pluginId, new ChangeRecord
            {
                Action = action,
                Target = BuildAdsPath(filePath, streamName),
                OldValue = oldValue,
            });
        }

        /// <summary>尝试读取现有 ADS 内容，失败返回 null（不抛异常）</summary>
        private static string? TryReadExistingContent(string filePath, string streamName)
        {
            try
            {
                var adsPath = BuildAdsPath(filePath, streamName);
                var handle = CreateFileW(
                    adsPath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    IsDirectory(filePath) ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    return TryReadFallback(filePath);

                try
                {
                    return ReadStreamContent(handle);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
