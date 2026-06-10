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
        private readonly RollbackEngine _rollback;
        private readonly object _lock = new();

        public RegistryService(RollbackEngine rollback)
        {
            _rollback = rollback;
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

                    var pluginId = PluginAccessContext.CurrentPluginId ?? "builtin";

                    _rollback.RecordChange(pluginId, new ChangeRecord
                    {
                        Action = ChangeAction.RegistryWrite,
                        Target = fullPath,
                        ValueName = valueName,
                        OldValue = oldValue,
                        NewValue = value,
                    });

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

                    var pluginId = PluginAccessContext.CurrentPluginId ?? "builtin";

                    _rollback.RecordChange(pluginId, new ChangeRecord
                    {
                        Action = ChangeAction.RegistryDelete,
                        Target = fullPath,
                        ValueName = valueName,
                        OldValue = oldValue,
                    });

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
            return _rollback.RollbackAsync(pluginId);
        }

        private static string ResolveKeyPath(string key)
        {
            return string.IsNullOrEmpty(key)
                ? RootKeyPath
                : $@"{RootKeyPath}\{key.Trim('\\')}";
        }
    }
}
