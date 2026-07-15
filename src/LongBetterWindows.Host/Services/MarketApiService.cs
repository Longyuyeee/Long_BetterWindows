using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 插件市场 API 服务 - 支持本地 JSON 和远程 API
    /// </summary>
    public class MarketApiService
    {
        private static readonly HttpClient _httpClient = new();
        private static MarketData? _cachedData;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);

        private const string LocalDataPath = "market-data.json";

        /// <summary>
        /// 获取所有插件（支持分类过滤）
        /// </summary>
        public static async Task<List<MarketPlugin>> GetPluginsAsync(string? category = null)
        {
            var data = await LoadMarketDataAsync();
            var plugins = data.Plugins;

            if (!string.IsNullOrEmpty(category))
            {
                plugins = plugins.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return plugins;
        }

        /// <summary>
        /// 获取插件详情
        /// </summary>
        public static async Task<MarketPlugin?> GetPluginDetailAsync(string pluginId)
        {
            var data = await LoadMarketDataAsync();
            return data.Plugins.FirstOrDefault(p => p.Id == pluginId);
        }

        /// <summary>
        /// 搜索插件
        /// </summary>
        public static async Task<List<MarketPlugin>> SearchPluginsAsync(string keyword)
        {
            var data = await LoadMarketDataAsync();
            var lowerKeyword = keyword.ToLower();

            return data.Plugins
                .Where(p =>
                    p.Name.ToLower().Contains(lowerKeyword) ||
                    p.Description.ToLower().Contains(lowerKeyword) ||
                    p.Author.ToLower().Contains(lowerKeyword) ||
                    p.Tags.Any(t => t.ToLower().Contains(lowerKeyword)))
                .ToList();
        }

        /// <summary>
        /// 获取精选插件
        /// </summary>
        public static async Task<List<MarketPlugin>> GetFeaturedPluginsAsync()
        {
            var data = await LoadMarketDataAsync();
            var featuredIds = data.Featured;

            return data.Plugins
                .Where(p => featuredIds.Contains(p.Id))
                .ToList();
        }

        /// <summary>
        /// 检查更新
        /// </summary>
        public static async Task<List<(string PluginId, string NewVersion, MarketPlugin Plugin)>> GetUpdatesAsync(
            Dictionary<string, string> installedPlugins)
        {
            var data = await LoadMarketDataAsync();
            var updates = new List<(string, string, MarketPlugin)>();

            foreach (var (pluginId, currentVersion) in installedPlugins)
            {
                var marketPlugin = data.Plugins.FirstOrDefault(p => p.Id == pluginId);
                if (marketPlugin == null) continue;

                if (IsNewerVersion(marketPlugin.Version, currentVersion))
                {
                    updates.Add((pluginId, marketPlugin.Version, marketPlugin));
                }
            }

            return updates;
        }

        /// <summary>
        /// 下载插件
        /// </summary>
        public static async Task<byte[]> DownloadPluginAsync(string downloadUrl, IProgress<double>? progress = null)
        {
            try
            {
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var memoryStream = new MemoryStream();

                var buffer = new byte[8192];
                long bytesRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)bytesRead / totalBytes * 100);
                    }
                }

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "下载插件失败: {Url}", downloadUrl);
                throw;
            }
        }

        /// <summary>
        /// 获取所有分类
        /// </summary>
        public static async Task<List<string>> GetCategoriesAsync()
        {
            var data = await LoadMarketDataAsync();
            return data.Categories;
        }

        /// <summary>
        /// 加载市场数据（本地 JSON 或远程 API）
        /// </summary>
        private static async Task<MarketData> LoadMarketDataAsync()
        {
            // 检查缓存
            if (_cachedData != null && DateTime.Now - _cacheTime < _cacheExpiration)
            {
                return _cachedData;
            }

            try
            {
                // 优先读取本地 JSON
                if (File.Exists(LocalDataPath))
                {
                    var json = await File.ReadAllTextAsync(LocalDataPath);
                    _cachedData = JsonSerializer.Deserialize<MarketData>(json);
                    _cacheTime = DateTime.Now;
                    Log.Information("已加载本地插件市场数据: {Count} 个插件", _cachedData?.Plugins.Count ?? 0);
                    return _cachedData ?? new MarketData();
                }

                // TODO: 未来支持远程 API
                // var response = await _httpClient.GetStringAsync("https://api.longbetterwindows.com/market");
                // _cachedData = JsonSerializer.Deserialize<MarketData>(response);

                Log.Warning("未找到插件市场数据文件: {Path}", LocalDataPath);
                return new MarketData();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载插件市场数据失败");
                return new MarketData();
            }
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public static void ClearCache()
        {
            _cachedData = null;
            _cacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// 版本比较
        /// </summary>
        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            var newParts = ParseVersion(newVersion);
            var currentParts = ParseVersion(currentVersion);

            if (newParts.Major > currentParts.Major) return true;
            if (newParts.Major < currentParts.Major) return false;

            if (newParts.Minor > currentParts.Minor) return true;
            if (newParts.Minor < currentParts.Minor) return false;

            return newParts.Patch > currentParts.Patch;
        }

        private static (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            var parts = version.Split('.');
            var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
            return (major, minor, patch);
        }
    }
}
