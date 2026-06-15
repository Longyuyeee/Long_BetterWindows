using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using Serilog;
using static LongBetterWindows.Host.Services.NativeMethods;

namespace LongBetterWindows.Host.Services
{
    public class RollbackEngine
    {
        private readonly string _logDir;
        private readonly object _lock = new();
        private readonly Dictionary<string, PluginChangeLog> _logs = new();

        public RollbackEngine(string? logDir = null)
        {
            _logDir = logDir ?? Path.Combine(
                AppContext.BaseDirectory, "config", "rollback");

            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);

            LoadAllLogs();
        }

        public void RecordChange(string pluginId, ChangeRecord record)
        {
            lock (_lock)
            {
                if (!_logs.TryGetValue(pluginId, out var log))
                {
                    log = new PluginChangeLog { PluginId = pluginId };
                    _logs[pluginId] = log;
                }

                record.Timestamp = DateTime.UtcNow;
                log.Records.Add(record);
                SaveLog(pluginId);

                Log.Debug("变更已记录: {PluginId} -> {Action} {Target}",
                    pluginId, record.Action, record.Target);
            }
        }

        public IReadOnlyList<ChangeRecord> GetPluginChanges(string pluginId)
        {
            lock (_lock)
            {
                if (_logs.TryGetValue(pluginId, out var log))
                {
                    return log.Records.ToList();
                }

                return Array.Empty<ChangeRecord>();
            }
        }

        public IReadOnlyList<string> GetActivePluginIds()
        {
            lock (_lock)
            {
                return _logs.Keys.ToList();
            }
        }

        public Task<HostApiResponse> RollbackAsync(string pluginId)
        {
            return Task.Run(() => RollbackInternal(pluginId));
        }

        private HostApiResponse RollbackInternal(string pluginId)
        {
            List<ChangeRecord> records;

            lock (_lock)
            {
                if (!_logs.TryGetValue(pluginId, out var log))
                {
                    return HostApiResponse.Success();
                }

                records = log.Records
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();
            }

            Log.Information("开始回滚插件 {PluginId}，共 {Count} 条变更",
                pluginId, records.Count);

            int rolledBack = 0;
            int failed = 0;

            foreach (var record in records)
            {
                try
                {
                    bool ok = record.Action switch
                    {
                        ChangeAction.RegistryWrite => RollbackRegistryWrite(record),
                        ChangeAction.RegistryDelete => RollbackRegistryDelete(record),
                        ChangeAction.AdsWrite => RollbackAdsWrite(record),
                        ChangeAction.AdsDelete => RollbackAdsDelete(record),
                        _ => true,
                    };

                    if (ok) rolledBack++;
                    else failed++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "回滚操作失败: {Action} {Target}",
                        record.Action, record.Target);
                    failed++;
                }
            }

            lock (_lock)
            {
                _logs.Remove(pluginId);
                DeleteLogFile(pluginId);
            }

            Log.Information("插件 {PluginId} 回滚完成: 成功={RolledBack}, 失败={Failed}",
                pluginId, rolledBack, failed);

            if (failed > 0)
            {
                return HostApiResponse.Failure(ApiErrorCode.Unknown,
                    $"回滚部分失败: {failed}/{records.Count} 条记录出错。");
            }

            return HostApiResponse.Success();
        }

        private bool RollbackRegistryWrite(ChangeRecord record)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(record.Target, writable: true);

            if (key == null)
                return false;

            if (record.OldValue == null)
            {
                key.DeleteValue(record.ValueName ?? "", throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(record.ValueName, record.OldValue);
            }

            return true;
        }

        private bool RollbackRegistryDelete(ChangeRecord record)
        {
            if (record.OldValue == null)
                return true;

            using var key = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(record.Target);

            if (key == null)
                return false;

            key.SetValue(record.ValueName, record.OldValue);
            return true;
        }

        private bool RollbackAdsWrite(ChangeRecord record)
        {
            try
            {
                if (!string.IsNullOrEmpty(record.OldValue))
                {
                    // 存在旧内容 → 恢复旧内容
                    return WriteAdsContent(record.Target, record.OldValue);
                }
                else
                {
                    // 无旧内容（新建的流）→ 删除该 ADS 流
                    if (!DeleteFileW(record.Target))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 2 && error != 3) // 不是"文件不存在"
                        {
                            Log.Warning("ADS 回滚删除失败: {Target}, Win32Error={Error}", record.Target, error);
                        }
                    }

                    // 同时清理回退文件
                    DeleteAdsFallback(record.Target);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ADS 回滚写操作失败: {Target}", record.Target);
                return false;
            }
        }

        private bool RollbackAdsDelete(ChangeRecord record)
        {
            try
            {
                if (!string.IsNullOrEmpty(record.OldValue))
                {
                    // 恢复了被删除的内容
                    return WriteAdsContent(record.Target, record.OldValue);
                }
                // 无旧内容（流原本就不存在），无需恢复
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ADS 回滚删操作失败: {Target}", record.Target);
                return false;
            }
        }

        /// <summary>向 ADS 流写入内容，失败时回退到 fallback 文件</summary>
        private static bool WriteAdsContent(string adsPath, string content)
        {
            // 判断是否为目录（ADS 路径格式: path:stream）
            var basePath = GetBasePathFromAds(adsPath);
            bool isDir = !string.IsNullOrEmpty(basePath) && Directory.Exists(basePath);

            var handle = CreateFileW(
                adsPath,
                GENERIC_WRITE,
                FILE_SHARE_READ,
                IntPtr.Zero,
                CREATE_ALWAYS,
                isDir ? FILE_FLAG_BACKUP_SEMANTICS : FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == (IntPtr)INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
            {
                // ADS 写入失败，写入 fallback 文件
                WriteAdsFallback(adsPath, content);
                return true;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                if (!WriteFile(handle, bytes, (uint)bytes.Length, out _, IntPtr.Zero))
                {
                    WriteAdsFallback(adsPath, content);
                }
            }
            finally
            {
                CloseHandle(handle);
            }

            return true;
        }

        /// <summary>从 ADS 路径提取基础文件路径（去掉 :stream 后缀）</summary>
        private static string GetBasePathFromAds(string adsPath)
        {
            var colonIdx = adsPath.LastIndexOf(':');
            return colonIdx > 0 ? adsPath.Substring(0, colonIdx) : adsPath;
        }

        /// <summary>获取 ADS 对应的 fallback 文件路径</summary>
        private static string GetAdsFallbackPath(string adsPath)
        {
            var basePath = GetBasePathFromAds(adsPath);
            var dir = Directory.Exists(basePath) ? basePath : Path.GetDirectoryName(basePath);
            return Path.Combine(dir ?? ".", "long_note.json");
        }

        private static void DeleteAdsFallback(string adsPath)
        {
            var fallbackPath = GetAdsFallbackPath(adsPath);
            try
            {
                if (File.Exists(fallbackPath))
                    File.Delete(fallbackPath);
            }
            catch { }
        }

        private static void WriteAdsFallback(string adsPath, string content)
        {
            var fallbackPath = GetAdsFallbackPath(adsPath);
            try
            {
                var dir = Path.GetDirectoryName(fallbackPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fallbackPath, content, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ADS fallback 写入失败: {Path}", fallbackPath);
            }
        }

        private string GetLogPath(string pluginId)
        {
            var safeName = pluginId.Replace('.', '_').Replace('\\', '_').Replace('/', '_');
            return Path.Combine(_logDir, $"{safeName}.json");
        }

        private void SaveLog(string pluginId)
        {
            if (_logs.TryGetValue(pluginId, out var log))
            {
                var json = JsonSerializer.Serialize(log,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetLogPath(pluginId), json);
            }
        }

        private void LoadAllLogs()
        {
            if (!Directory.Exists(_logDir))
                return;

            foreach (var file in Directory.GetFiles(_logDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var log = JsonSerializer.Deserialize<PluginChangeLog>(json);
                    if (log != null && !string.IsNullOrEmpty(log.PluginId))
                    {
                        _logs[log.PluginId] = log;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "加载回滚日志失败: {File}", file);
                }
            }

            Log.Debug("加载了 {Count} 个插件的回滚日志", _logs.Count);
        }

        private void DeleteLogFile(string pluginId)
        {
            var path = GetLogPath(pluginId);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "删除回滚日志文件失败: {Path}", path);
            }
        }
    }

    public class PluginChangeLog
    {
        public string PluginId { get; init; } = string.Empty;
        public List<ChangeRecord> Records { get; init; } = new();
    }

    public class ChangeRecord
    {
        public DateTime Timestamp { get; set; }
        public ChangeAction Action { get; init; }
        public string Target { get; init; } = string.Empty;
        public string? ValueName { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
    }

    public enum ChangeAction
    {
        RegistryWrite,
        RegistryDelete,
        AdsWrite,
        AdsDelete,
    }
}
