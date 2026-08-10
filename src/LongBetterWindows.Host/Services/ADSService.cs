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
    public sealed class ADSService : IADSService
    {
        private const string DefaultStreamName = "long_note";
        private const int MaxContentBytes = 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly RollbackEngine _rollback;
        private readonly object _mutationGate;

        public ADSService(RollbackEngine rollback)
        {
            _rollback = rollback;
            _mutationGate = rollback.AdsMutationGate;
        }

        public Task<HostApiResponse<string>> ReadAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                lock (_mutationGate)
                {
                    try
                    {
                        var targetPath = ResolveTarget(filePath);
                        EnsureNtfsVolume(targetPath);
                        var snapshot = ReadSnapshot(targetPath, streamName);
                        return snapshot.Exists
                            ? HostApiResponse<string>.Success(snapshot.Content ?? string.Empty)
                            : HostApiResponse<string>.Failure(
                                ApiErrorCode.StreamNotFound,
                                "备用数据流不存在。");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ADS 读取失败: {Path}:{Stream}", filePath, streamName);
                        return HostApiResponse<string>.Failure(
                            MapError(ex),
                            ex.Message);
                    }
                }
            });

        public Task<HostApiResponse> WriteAsync(
            string filePath,
            string streamName,
            string content)
            => Task.Run(() =>
            {
                lock (_mutationGate)
                {
                    StorageSnapshot? oldSnapshot = null;
                    string? targetPath = null;
                    try
                    {
                        ArgumentNullException.ThrowIfNull(content);
                        targetPath = ResolveTarget(filePath);
                        EnsureNtfsVolume(targetPath);
                        var bytes = StrictUtf8.GetBytes(content);
                        if (bytes.Length > MaxContentBytes)
                        {
                            return HostApiResponse.Failure(
                                ApiErrorCode.InvalidArgument,
                                $"ADS 内容不能超过 {MaxContentBytes} 字节。");
                        }

                        oldSnapshot = ReadSnapshot(targetPath, streamName);
                        ResolveTarget(targetPath);
                        var target = BuildAdsPath(targetPath, streamName);

                        WriteStorage(target, bytes);
                        var written = ReadStorage(target);
                        if (!written.AsSpan().SequenceEqual(bytes))
                            throw new IOException("ADS 写入后的回读校验失败。");

                        RecordAdsChange(
                            ChangeAction.AdsWrite,
                            targetPath,
                            streamName,
                            target,
                            oldSnapshot,
                            content);
                        return HostApiResponse.Success();
                    }
                    catch (Exception ex)
                    {
                        var restored = RestoreSnapshotAfterFailure(
                            targetPath ?? filePath,
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
                }
            });

        public Task<HostApiResponse> DeleteAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                lock (_mutationGate)
                {
                    StorageSnapshot? oldSnapshot = null;
                    var storageDeleted = false;
                    string? targetPath = null;
                    try
                    {
                        targetPath = ResolveTarget(filePath);
                        EnsureNtfsVolume(targetPath);
                        oldSnapshot = ReadSnapshot(targetPath, streamName);
                        if (!oldSnapshot.Exists)
                            return HostApiResponse.Success();

                        ResolveTarget(targetPath);
                        DeleteStorage(oldSnapshot.StoragePath);
                        storageDeleted = true;
                        if (StorageExists(oldSnapshot.StoragePath))
                            throw new IOException("删除备注后存储仍然存在。");

                        RecordAdsChange(
                            ChangeAction.AdsDelete,
                            targetPath,
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
                                targetPath ?? filePath,
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
                }
            });

        public Task<HostApiResponse<bool>> ExistsAsync(
            string filePath,
            string streamName)
            => Task.Run(() =>
            {
                lock (_mutationGate)
                {
                    try
                    {
                        var targetPath = ResolveTarget(filePath);
                        EnsureNtfsVolume(targetPath);
                        return HostApiResponse<bool>.Success(
                            ReadSnapshot(targetPath, streamName).Exists);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ADS 存在检查失败: {Path}:{Stream}", filePath, streamName);
                        return HostApiResponse<bool>.Failure(MapError(ex), ex.Message);
                    }
                }
            });

        public Task<HostApiResponse<bool>> IsNTFSVolumeAsync(string filePath)
            => Task.Run(() =>
            {
                try
                {
                    var targetPath = ResolveTarget(filePath);
                    return HostApiResponse<bool>.Success(IsNtfsVolume(targetPath));
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
            return TryReadAds(adsPath);
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
                    StrictUtf8.GetString(bytes),
                    adsPath);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static void WriteStorage(
            string storagePath,
            byte[] bytes)
        {
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

        private static byte[] ReadStorage(string storagePath)
        {
            var snapshot = TryReadAds(storagePath);
            if (!snapshot.Exists)
                throw new FileNotFoundException("ADS 回读时不存在。", storagePath);
            return StrictUtf8.GetBytes(snapshot.Content ?? string.Empty);
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
                if (stream.Length > MaxContentBytes)
                    throw new InvalidDataException(
                        $"ADS 内容不能超过 {MaxContentBytes} 字节。");
            }

            return stream.ToArray();
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
                if (snapshot.Exists)
                {
                    var bytes = StrictUtf8.GetBytes(snapshot.Content ?? string.Empty);
                    WriteStorage(snapshot.StoragePath, bytes);
                }
                else
                {
                    DeleteStorage(adsPath);
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

        private static string ResolveTarget(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("目标路径不能为空。", nameof(filePath));
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException("目标文件或文件夹不存在。", fullPath);
            EnsureNoReparsePoint(fullPath);
            return fullPath;
        }

        private static void EnsureNoReparsePoint(string fullPath)
        {
            var current = fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException(
                        "ADS 目标路径不能经过重解析点。");

                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent)
                    || parent.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent;
            }
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

        internal static string ValidateRollbackTarget(string adsPath)
        {
            if (string.IsNullOrWhiteSpace(adsPath))
                throw new InvalidDataException("ADS rollback target is empty.");

            var root = Path.GetPathRoot(adsPath);
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidDataException("ADS rollback target is not rooted.");

            var streamSeparator = adsPath.IndexOf(':', root.Length);
            if (streamSeparator < root.Length
                || streamSeparator == adsPath.Length - 1)
            {
                throw new InvalidDataException(
                    "ADS rollback target is not an alternate data stream.");
            }

            var filePath = adsPath[..streamSeparator];
            var streamName = adsPath[(streamSeparator + 1)..];
            var resolvedPath = ResolveTarget(filePath);
            EnsureNtfsVolume(resolvedPath);
            var canonicalTarget = BuildAdsPath(resolvedPath, streamName);
            if (!canonicalTarget.Equals(
                    adsPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "ADS rollback target is not canonical.");
            }

            return canonicalTarget;
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

        private static void EnsureNtfsVolume(string filePath)
        {
            if (!IsNtfsVolume(filePath))
                throw new NotNtfsVolumeException(
                    "当前文件系统不支持 NTFS 备用数据流。");
        }

        private static bool StorageExists(string path)
            => TryReadAds(path).Exists;

        private static void DeleteStorage(string path)
        {
            if (!DeleteFileW(path))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is not 2 and not 3)
                    throw CreateIoException("ADS 删除失败", error);
            }
        }

        private static IOException CreateIoException(string operation, int error)
            => new($"{operation} (Win32: {error})。");

        private static ApiErrorCode MapError(Exception exception)
            => exception switch
            {
                ArgumentException => ApiErrorCode.InvalidArgument,
                InvalidDataException or DecoderFallbackException
                    => ApiErrorCode.InvalidArgument,
                NotNtfsVolumeException => ApiErrorCode.NotNTFSVolume,
                FileNotFoundException or DirectoryNotFoundException
                    => ApiErrorCode.NotFound,
                UnauthorizedAccessException => ApiErrorCode.PermissionDenied,
                _ => ApiErrorCode.Win32Error,
            };

        private sealed class NotNtfsVolumeException(string message)
            : IOException(message);

        private sealed record StorageSnapshot(
            bool Exists,
            string? Content,
            string StoragePath);
    }
}
