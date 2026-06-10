using Microsoft.Win32;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    public class RegistryService : IRegistryService
    {
        private const string RootKeyPath = @"Software\LongBetterWindows";

        private readonly List<RegistryChangeRecord> _changes = new();
        private readonly object _lock = new();

        public IReadOnlyList<RegistryChangeRecord> Changes
        {
            get { lock (_lock) return _changes.ToList(); }
        }

        public IReadOnlyList<RegistryChangeRecord> GetChangesForPlugin(string pluginId)
        {
            lock (_lock)
            {
                return _changes
                    .Where(c => c.PluginId == pluginId)
                    .ToList();
            }
        }

        public void RemoveChangesForPlugin(string pluginId)
        {
            lock (_lock)
            {
                _changes.RemoveAll(c => c.PluginId == pluginId);
            }
        }

        public Task<HostApiResponse<string?>> ReadValueAsync(string key, string valueName)
        {
            return Task.Run(() =>
            {
                try
                {
                    var fullPath = ResolveKeyPath(key);
                    using var regKey = Registry.CurrentUser.OpenSubKey(fullPath);

                    if (regKey == null)
                    {
                        return HostApiResponse<string?>.Success(null);
                    }

                    var value = regKey.GetValue(valueName);
                    return HostApiResponse<string?>.Success(value?.ToString());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "注册表读取失败: {Key}\\{ValueName}", key, valueName);
                    return HostApiResponse<string?>.Failure(
                        ApiErrorCode.RegistryAccessDenied, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> WriteValueAsync(string key, string valueName, string value)
        {
            return Task.Run(() =>
            {
                try
                {
                    var fullPath = ResolveKeyPath(key);
                    string? oldValue = null;

                    using (var regKey = Registry.CurrentUser.OpenSubKey(fullPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(fullPath))
                    {
                        if (regKey == null)
                        {
                            return HostApiResponse.Failure(
                                ApiErrorCode.RegistryAccessDenied, "无法打开或创建注册表键。");
                        }

                        oldValue = regKey.GetValue(valueName)?.ToString();
                        regKey.SetValue(valueName, value);
                    }

                    var pluginId = PluginAccessContext.CurrentPluginId ?? "unknown";

                    lock (_lock)
                    {
                        _changes.Add(new RegistryChangeRecord
                        {
                            PluginId = pluginId,
                            Timestamp = DateTime.UtcNow,
                            Action = RegistryAction.Write,
                            Key = key,
                            ValueName = valueName,
                            OldValue = oldValue,
                            NewValue = value,
                        });
                    }

                    Log.Debug("注册表写入: {Key}\\{ValueName} = {Value}", key, valueName, value);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "注册表写入失败: {Key}\\{ValueName}", key, valueName);
                    return HostApiResponse.Failure(
                        ApiErrorCode.RegistryAccessDenied, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> DeleteValueAsync(string key, string valueName)
        {
            return Task.Run(() =>
            {
                try
                {
                    var fullPath = ResolveKeyPath(key);
                    string? oldValue = null;

                    using (var regKey = Registry.CurrentUser.OpenSubKey(fullPath, writable: true))
                    {
                        if (regKey == null)
                        {
                            return HostApiResponse.Success();
                        }

                        oldValue = regKey.GetValue(valueName)?.ToString();
                        regKey.DeleteValue(valueName, throwOnMissingValue: false);
                    }

                    var pluginId = PluginAccessContext.CurrentPluginId ?? "unknown";

                    lock (_lock)
                    {
                        _changes.Add(new RegistryChangeRecord
                        {
                            PluginId = pluginId,
                            Timestamp = DateTime.UtcNow,
                            Action = RegistryAction.Delete,
                            Key = key,
                            ValueName = valueName,
                            OldValue = oldValue,
                        });
                    }

                    Log.Debug("注册表删除: {Key}\\{ValueName}", key, valueName);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "注册表删除失败: {Key}\\{ValueName}", key, valueName);
                    return HostApiResponse.Failure(
                        ApiErrorCode.RegistryAccessDenied, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> RollbackAsync(string pluginId)
        {
            return Task.Run(() =>
            {
                List<RegistryChangeRecord> pluginChanges;

                lock (_lock)
                {
                    pluginChanges = _changes
                        .Where(c => c.PluginId == pluginId)
                        .OrderByDescending(c => c.Timestamp)
                        .ToList();
                }

                Log.Information("开始回滚插件 {PluginId} 的 {Count} 条注册表变更",
                    pluginId, pluginChanges.Count);

                foreach (var change in pluginChanges)
                {
                    try
                    {
                        var fullPath = ResolveKeyPath(change.Key);

                        using var regKey = Registry.CurrentUser.OpenSubKey(
                            fullPath, writable: true);

                        if (regKey == null)
                            continue;

                        if (change.Action == RegistryAction.Write)
                        {
                            if (change.OldValue == null)
                            {
                                regKey.DeleteValue(change.ValueName, throwOnMissingValue: false);
                            }
                            else
                            {
                                regKey.SetValue(change.ValueName, change.OldValue);
                            }
                        }
                        else if (change.Action == RegistryAction.Delete)
                        {
                            if (change.OldValue != null)
                            {
                                regKey.SetValue(change.ValueName, change.OldValue);
                            }
                        }

                        Log.Debug("回滚: {Key}\\{ValueName}", change.Key, change.ValueName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "回滚失败: {Key}\\{ValueName}", change.Key, change.ValueName);
                    }
                }

                lock (_lock)
                {
                    _changes.RemoveAll(c => c.PluginId == pluginId);
                }

                Log.Information("插件 {PluginId} 注册表回滚完成", pluginId);
                return HostApiResponse.Success();
            });
        }

        private static string ResolveKeyPath(string key)
        {
            return string.IsNullOrEmpty(key)
                ? RootKeyPath
                : $@"{RootKeyPath}\{key.Trim('\\')}";
        }
    }

    public enum RegistryAction
    {
        Write,
        Delete,
    }

    public class RegistryChangeRecord
    {
        public string PluginId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public RegistryAction Action { get; init; }
        public string Key { get; init; } = string.Empty;
        public string ValueName { get; init; } = string.Empty;
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
    }
}
