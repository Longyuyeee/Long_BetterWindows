using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 插件更新检测服务
    /// </summary>
    public class PluginUpdateService
    {
        private static readonly object _lock = new();
        private static List<PluginUpdateInfo>? _cachedUpdates;
        private static DateTime _lastCheckTime = DateTime.MinValue;
        private static readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);

        /// <summary>
        /// 检查所有插件的更新
        /// </summary>
        public static async Task<List<PluginUpdateInfo>> CheckUpdatesAsync(bool forceRefresh = false)
        {
            // 检查缓存
            if (!forceRefresh && _cachedUpdates != null && DateTime.Now - _lastCheckTime < _checkInterval)
            {
                Log.Debug("使用缓存的更新列表");
                return _cachedUpdates;
            }

            try
            {
                var updates = new List<PluginUpdateInfo>();

                // 获取已安装的插件
                var installedPlugins = GetInstalledPlugins();

                // 从市场获取所有插件
                var marketPlugins = await MarketApiService.GetPluginsAsync();

                foreach (var installed in installedPlugins)
                {
                    var marketPlugin = marketPlugins.FirstOrDefault(m => m.Id == installed.Id);
                    if (marketPlugin == null)
                        continue;

                    // 比较版本
                    if (IsNewerVersion(marketPlugin.Version, installed.Version))
                    {
                        updates.Add(new PluginUpdateInfo
                        {
                            PluginId = installed.Id,
                            CurrentVersion = installed.Version,
                            NewVersion = marketPlugin.Version,
                            PluginName = installed.Name,
                            UpdateDescription = $"从 {installed.Version} 更新到 {marketPlugin.Version}",
                            MarketPlugin = marketPlugin
                        });

                        Log.Information("发现插件更新: {PluginId} {OldVersion} -> {NewVersion}",
                            installed.Id, installed.Version, marketPlugin.Version);
                    }
                }

                // 更新缓存
                lock (_lock)
                {
                    _cachedUpdates = updates;
                    _lastCheckTime = DateTime.Now;
                }

                return updates;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查插件更新失败");
                return new List<PluginUpdateInfo>();
            }
        }

        /// <summary>
        /// 获取已安装的插件列表
        /// </summary>
        private static List<InstalledPluginInfo> GetInstalledPlugins()
        {
            var result = new List<InstalledPluginInfo>();
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

            if (!Directory.Exists(pluginsDir))
                return result;

            foreach (var dir in Directory.GetDirectories(pluginsDir))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                    if (manifest != null)
                    {
                        result.Add(new InstalledPluginInfo
                        {
                            Id = manifest.Id,
                            Name = manifest.Name,
                            Version = manifest.Version
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "读取插件 manifest 失败: {Path}", manifestPath);
                }
            }

            return result;
        }

        /// <summary>
        /// 比较版本号 (支持语义化版本)
        /// </summary>
        private static bool IsNewerVersion(string marketVersion, string installedVersion)
        {
            try
            {
                var market = ParseVersion(marketVersion);
                var installed = ParseVersion(installedVersion);

                if (market.Major > installed.Major) return true;
                if (market.Major < installed.Major) return false;

                if (market.Minor > installed.Minor) return true;
                if (market.Minor < installed.Minor) return false;

                if (market.Patch > installed.Patch) return true;

                return false;
            }
            catch
            {
                // 如果解析失败，使用字符串比较
                return string.Compare(marketVersion, installedVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        /// <summary>
        /// 解析版本号
        /// </summary>
        private static (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            var parts = version.Split('.');
            var major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
            var minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            var patch = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            return (major, minor, patch);
        }

        /// <summary>
        /// 清除更新缓存
        /// </summary>
        public static void ClearCache()
        {
            lock (_lock)
            {
                _cachedUpdates = null;
                _lastCheckTime = DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// 插件更新信息
    /// </summary>
    public class PluginUpdateInfo
    {
        public string PluginId { get; set; } = "";
        public string PluginName { get; set; } = "";
        public string CurrentVersion { get; set; } = "";
        public string NewVersion { get; set; } = "";
        public string UpdateDescription { get; set; } = "";
        public MarketPlugin? MarketPlugin { get; set; }
    }

    /// <summary>
    /// 已安装插件信息
    /// </summary>
    internal class InstalledPluginInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }
}
