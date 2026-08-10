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

        internal object AdsMutationGate { get; } = new();

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
                var createdLog = false;
                if (!_logs.TryGetValue(pluginId, out var log))
                {
                    log = new PluginChangeLog { PluginId = pluginId };
                    _logs[pluginId] = log;
                    createdLog = true;
                }

                record.Timestamp = DateTime.UtcNow;
                log.Records.Add(record);
                try
                {
                    SaveLog(pluginId);
                }
                catch
                {
                    log.Records.Remove(record);
                    if (createdLog && log.Records.Count == 0)
                        _logs.Remove(pluginId);
                    throw;
                }

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
            var failedRecords = new List<ChangeRecord>();

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

                    if (ok)
                    {
                        rolledBack++;
                    }
                    else
                    {
                        failed++;
                        failedRecords.Add(record);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "回滚操作失败: {Action} {Target}",
                        record.Action, record.Target);
                    failed++;
                    failedRecords.Add(record);
                }
            }

            lock (_lock)
            {
                if (failedRecords.Count == 0)
                {
                    _logs.Remove(pluginId);
                    DeleteLogFile(pluginId);
                }
                else if (_logs.TryGetValue(pluginId, out var log))
                {
                    log.Records.Clear();
                    log.Records.AddRange(failedRecords);
                    try
                    {
                        SaveLog(pluginId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            ex,
                            "失败回滚记录无法持久化，将保留在当前进程内: {PluginId}",
                            pluginId);
                    }
                }
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
            lock (AdsMutationGate)
            {
                try
                {
                    var currentTarget = ResolveAdsRollbackTarget(
                        record,
                        record.StorageTarget);
                    string? oldTarget = null;
                    if (record.OldValueExists ?? record.OldValue is not null)
                    {
                        oldTarget = ResolveAdsRollbackTarget(
                            record,
                            record.OldStorageTarget);
                    }

                    if (oldTarget is null)
                        DeleteStorageTarget(currentTarget);
                    else
                        WriteStorageTarget(oldTarget, record.OldValue ?? string.Empty);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 回滚写操作失败: {Target}", record.Target);
                    return false;
                }
            }
        }

        private bool RollbackAdsDelete(ChangeRecord record)
        {
            lock (AdsMutationGate)
            {
                try
                {
                    if (record.OldValueExists ?? record.OldValue is not null)
                    {
                        WriteStorageTarget(
                            ResolveAdsRollbackTarget(
                                record,
                                record.OldStorageTarget ?? record.StorageTarget),
                            record.OldValue ?? string.Empty);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ADS 回滚删操作失败: {Target}", record.Target);
                    return false;
                }
            }
        }

        private static string ResolveAdsRollbackTarget(
            ChangeRecord record,
            string? storageTarget)
        {
            var target = record.Target;
            var candidate = storageTarget ?? target;
            if (!candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "ADS rollback target must be the recorded alternate data stream.");
            }

            return ADSService.ValidateRollbackTarget(target);
        }

        private static void DeleteStorageTarget(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return;
            }

            if (!DeleteFileW(path))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is not 2 and not 3)
                    throw new IOException($"删除回滚目标失败 (Win32: {error})。");
            }
        }

        private static void WriteStorageTarget(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, new UTF8Encoding(false));
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
                var logPath = GetLogPath(pluginId);
                var temporaryPath = Path.Combine(
                    _logDir,
                    $".{Path.GetFileName(logPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(
                        temporaryPath,
                        json,
                        new UTF8Encoding(false));
                    File.Move(temporaryPath, logPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
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
        public string? StorageTarget { get; init; }
        public string? OldStorageTarget { get; init; }
        public bool? OldValueExists { get; init; }
    }

    public enum ChangeAction
    {
        RegistryWrite,
        RegistryDelete,
        AdsWrite,
        AdsDelete,
    }
}
