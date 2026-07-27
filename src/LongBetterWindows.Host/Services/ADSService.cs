using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;
using static LongBetterWindows.Host.Services.NativeMethods;

namespace LongBetterWindows.Host.Services
{
    public sealed class ADSService : IADSService
    {
        private const string DefaultStreamName = "long_note";
        private const string FolderNoteFallbackFileName = "long_note.json";
        private const int MaxContentBytes = 1024 * 1024;

        private readonly RollbackEngine _rollback;

        public ADSService(RollbackEngine rollback)
        {
            _rollback = rollback;
        }

        public Task<HostApiResponse<string>> ReadAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                try
                {
                    ValidateTarget(filePath);
                    var snapshot = ReadSnapshot(filePath, streamName);
                    return snapshot.Exists
                        ? HostApiResponse<string>.Success(snapshot.Content ?? string.Empty)
                        : HostApiResponse<string>.Failure(
                            ApiErrorCode.StreamNotFound,
                            "备用数据流不存在。");
                }
                catch (FileNotFoundException ex)
                {
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.NotFound,
                        ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.PermissionDenied,
                        ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 读取失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse<string>.Failure(
                        ApiErrorCode.Win32Error,
                        ex.Message);
                }
            });

        public Task<HostApiResponse> WriteAsync(
            string filePath,
            string streamName,
            string content)
            => Task.Run(() =>
            {
                StorageSnapshot? oldSnapshot = null;
                try
                {
                    ArgumentNullException.ThrowIfNull(content);
                    ValidateTarget(filePath);
                    var bytes = Encoding.UTF8.GetBytes(content);
                    if (bytes.Length > MaxContentBytes)
                    {
                        return HostApiResponse.Failure(
                            ApiErrorCode.InvalidArgument,
                            $"ADS 内容不能超过 {MaxContentBytes} 字节。");
                    }

                    oldSnapshot = ReadSnapshot(filePath, streamName);
                    var target = IsNtfsVolume(filePath)
                        ? BuildAdsPath(filePath, streamName)
                        : GetFallbackPath(filePath, streamName);

                    WriteStorage(target, bytes, IsAdsPath(target));
                    var written = ReadStorage(target, IsAdsPath(target));
                    if (!written.AsSpan().SequenceEqual(bytes))
                        throw new IOException("ADS 写入后的回读校验失败。");

                    if (oldSnapshot.Exists
                        && !PathEquals(oldSnapshot.StoragePath, target))
                    {
                        DeleteStorage(oldSnapshot.StoragePath);
                    }

                    RecordAdsChange(
                        ChangeAction.AdsWrite,
                        filePath,
                        streamName,
                        target,
                        oldSnapshot,
                        content);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    var restored = RestoreSnapshotAfterFailure(
                        filePath,
                        streamName,
                        oldSnapshot);
                    Log.Error(
                        ex,
                        "ADS 写入失败: {Path}:{Stream}; Restored={Restored}",
                        filePath,
                        streamName,
                        restored);
                    return HostApiResponse.Failure(
                        MapError(ex),
                        restored
                            ? ex.Message
                            : $"{ex.Message} 原备注恢复失败，请勿关闭编辑窗口。");
                }
            });

        public Task<HostApiResponse> DeleteAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                StorageSnapshot? oldSnapshot = null;
                var storageDeleted = false;
                try
                {
                    ValidateTarget(filePath);
                    oldSnapshot = ReadSnapshot(filePath, streamName);
                    if (!oldSnapshot.Exists)
                        return HostApiResponse.Success();

                    DeleteStorage(oldSnapshot.StoragePath);
                    storageDeleted = true;
                    if (StorageExists(oldSnapshot.StoragePath))
                        throw new IOException("删除备注后存储仍然存在。");

                    RecordAdsChange(
                        ChangeAction.AdsDelete,
                        filePath,
                        streamName,
                        oldSnapshot.StoragePath,
                        oldSnapshot,
                        null);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    var restored = !storageDeleted
                        || RestoreSnapshotAfterFailure(
                            filePath,
                            streamName,
                            oldSnapshot);
                    Log.Error(
                        ex,
                        "ADS 删除失败: {Path}:{Stream}; Restored={Restored}",
                        filePath,
                        streamName,
                        restored);
                    return HostApiResponse.Failure(
                        MapError(ex),
                        restored
                            ? ex.Message
                            : $"{ex.Message} 原备注恢复失败，请勿关闭编辑窗口。");
                }
            });

        public Task<HostApiResponse<bool>> ExistsAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                try
                {
                    ValidateTarget(filePath);
                    return HostApiResponse<bool>.Success(
                        ReadSnapshot(filePath, streamName).Exists);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 存在检查失败: {Path}:{Stream}", filePath, streamName);
                    return HostApiResponse<bool>.Failure(MapError(ex), ex.Message);
                }
            });

        public Task<HostApiResponse<bool>> IsNTFSVolumeAsync(string filePath)
            => Task.Run(() =>
            {
                try
                {
                    ValidateTarget(filePath);
                    return HostApiResponse<bool>.Success(IsNtfsVolume(filePath));
                }
                catch (Exception ex)
                {
                    return HostApiResponse<bool>.Failure(MapError(ex), ex.Message);
                }
            });

        private static StorageSnapshot ReadSnapshot(
            string filePath,
            string streamName)
        {
            var adsPath = BuildAdsPath(filePath, streamName);
            if (IsNtfsVolume(filePath))
            {
                var ads = TryReadAds(adsPath);
                if (ads.Exists)
                    return ads;
            }

            var fallbackPath = GetFallbackPath(filePath, streamName);
            if (File.Exists(fallbackPath))
            {
                return new StorageSnapshot(
                    true,
                    Encoding.UTF8.GetString(ReadStorage(fallbackPath, false)),
                    fallbackPath);
            }

            return new StorageSnapshot(false, null, adsPath);
        }

        private static StorageSnapshot TryReadAds(string adsPath)
        {
            var handle = CreateFileW(
                adsPath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 2 or 3)
                    return new StorageSnapshot(false, null, adsPath);
                throw CreateIoException("无法打开 ADS", error);
            }

            try
            {
                var bytes = ReadHandle(handle);
                return new StorageSnapshot(
                    true,
                    Encoding.UTF8.GetString(bytes),
                    adsPath);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static void WriteStorage(
            string storagePath,
            byte[] bytes,
            bool isAds)
        {
            if (!isAds)
            {
                WriteFallbackAtomically(storagePath, bytes);
                return;
            }

            var handle = CreateFileW(
                storagePath,
                GENERIC_WRITE,
                FILE_SHARE_READ,
                IntPtr.Zero,
                CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                throw CreateIoException(
                    "无法打开 ADS 进行写入",
                    Marshal.GetLastWin32Error());

            try
            {
                if (!WriteFile(
                        handle,
                        bytes,
                        (uint)bytes.Length,
                        out var bytesWritten,
                        IntPtr.Zero)
                    || bytesWritten != bytes.Length)
                {
                    throw CreateIoException(
                        "ADS 写入不完整",
                        Marshal.GetLastWin32Error());
                }

                if (!FlushFileBuffers(handle))
                {
                    throw CreateIoException(
                        "ADS 刷盘失败",
                        Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static byte[] ReadStorage(string storagePath, bool isAds)
        {
            if (!isAds)
                return File.ReadAllBytes(storagePath);

            var snapshot = TryReadAds(storagePath);
            if (!snapshot.Exists)
                throw new FileNotFoundException("ADS 回读时不存在。", storagePath);
            return Encoding.UTF8.GetBytes(snapshot.Content ?? string.Empty);
        }

        private static byte[] ReadHandle(IntPtr handle)
        {
            var buffer = new byte[4096];
            using var stream = new MemoryStream();
            while (true)
            {
                if (!ReadFile(
                        handle,
                        buffer,
                        (uint)buffer.Length,
                        out var bytesRead,
                        IntPtr.Zero))
                {
                    throw CreateIoException(
                        "ADS 读取失败",
                        Marshal.GetLastWin32Error());
                }

                if (bytesRead == 0)
                    break;
                stream.Write(buffer, 0, checked((int)bytesRead));
            }

            return stream.ToArray();
        }

        private static void WriteFallbackAtomically(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("回退文件目录无效。");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static bool RestoreSnapshotAfterFailure(
            string filePath,
            string streamName,
            StorageSnapshot? snapshot)
        {
            if (snapshot is null)
                return true;

            try
            {
                var adsPath = BuildAdsPath(filePath, streamName);
                var fallbackPath = GetFallbackPath(filePath, streamName);
                if (snapshot.Exists)
                {
                    var bytes = Encoding.UTF8.GetBytes(snapshot.Content ?? string.Empty);
                    WriteStorage(
                        snapshot.StoragePath,
                        bytes,
                        IsAdsPath(snapshot.StoragePath));
                    if (!PathEquals(snapshot.StoragePath, adsPath))
                        DeleteStorage(adsPath);
                    if (!PathEquals(snapshot.StoragePath, fallbackPath))
                        DeleteStorage(fallbackPath);
                }
                else
                {
                    DeleteStorage(adsPath);
                    DeleteStorage(fallbackPath);
                }

                return true;
            }
            catch (Exception restoreException)
            {
                Log.Error(
                    restoreException,
                    "ADS 失败恢复未完成: {Path}:{Stream}",
                    filePath,
                    streamName);
                return false;
            }
        }

        private void RecordAdsChange(
            ChangeAction action,
            string filePath,
            string streamName,
            string storageTarget,
            StorageSnapshot oldSnapshot,
            string? newValue)
        {
            var pluginId = PluginAccessContext.CurrentPluginId ?? "builtin";
            _rollback.RecordChange(pluginId, new ChangeRecord
            {
                Action = action,
                Target = BuildAdsPath(filePath, streamName),
                StorageTarget = storageTarget,
                OldStorageTarget = oldSnapshot.Exists
                    ? oldSnapshot.StoragePath
                    : null,
                OldValueExists = oldSnapshot.Exists,
                OldValue = oldSnapshot.Content,
                NewValue = newValue,
            });
        }

        private static void ValidateTarget(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("目标路径不能为空。", nameof(filePath));
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
                throw new FileNotFoundException("目标文件或文件夹不存在。", filePath);
        }

        private static string BuildAdsPath(string filePath, string streamName)
        {
            var name = string.IsNullOrEmpty(streamName)
                ? DefaultStreamName
                : streamName;
            if (name.Length > 255
                || name.Contains(':')
                || name.Contains('\\')
                || name.Contains('/')
                || name.Contains("..", StringComparison.Ordinal)
                || name.IndexOfAny(['<', '>', '|', '*', '?']) >= 0)
            {
                throw new ArgumentException("ADS 流名称无效。", nameof(streamName));
            }

            return $"{filePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)}:{name}";
        }

        private static string GetFallbackPath(
            string filePath,
            string streamName)
        {
            var name = string.IsNullOrEmpty(streamName)
                ? DefaultStreamName
                : streamName;
            if (Directory.Exists(filePath)
                && name.Equals(DefaultStreamName, StringComparison.Ordinal))
            {
                return Path.Combine(filePath, FolderNoteFallbackFileName);
            }

            var fullPath = Path.GetFullPath(filePath);
            var identity = Encoding.UTF8.GetBytes($"{fullPath}\0{name}");
            var suffix = Convert.ToHexString(SHA256.HashData(identity))
                .ToLowerInvariant()[..12];
            var directory = Directory.Exists(filePath)
                ? filePath
                : Path.GetDirectoryName(fullPath) ?? ".";
            return Path.Combine(directory, $"long_ads_{suffix}.json");
        }

        private static bool IsNtfsVolume(string filePath)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(filePath));
            if (string.IsNullOrWhiteSpace(root))
                return false;
            return new DriveInfo(root).DriveFormat.Equals(
                "NTFS",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdsPath(string path)
        {
            var rootLength = Path.GetPathRoot(path)?.Length ?? 0;
            return path.IndexOf(':', rootLength) >= 0;
        }

        private static bool StorageExists(string path)
        {
            if (!IsAdsPath(path))
                return File.Exists(path);
            return TryReadAds(path).Exists;
        }

        private static void DeleteStorage(string path)
        {
            if (!IsAdsPath(path))
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            if (!DeleteFileW(path))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is not 2 and not 3)
                    throw CreateIoException("ADS 删除失败", error);
            }
        }

        private static bool PathEquals(string left, string right)
            => left.Equals(right, StringComparison.OrdinalIgnoreCase);

        private static IOException CreateIoException(string operation, int error)
            => new($"{operation} (Win32: {error})。");

        private static ApiErrorCode MapError(Exception exception)
            => exception switch
            {
                ArgumentException => ApiErrorCode.InvalidArgument,
                FileNotFoundException or DirectoryNotFoundException
                    => ApiErrorCode.NotFound,
                UnauthorizedAccessException => ApiErrorCode.PermissionDenied,
                _ => ApiErrorCode.Win32Error,
            };

        private sealed record StorageSnapshot(
            bool Exists,
            string? Content,
            string StoragePath);
    }
}
