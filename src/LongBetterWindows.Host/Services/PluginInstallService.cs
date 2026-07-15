using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 插件安装服务 - 支持从市场下载和安装
    /// </summary>
    public class PluginInstallService
    {
        /// <summary>
        /// 从市场安装插件
        /// </summary>
        public static async Task<bool> InstallFromMarketAsync(
            MarketPlugin marketPlugin,
            IProgress<double>? progress = null,
            Action<string>? onStatusUpdate = null)
        {
            try
            {
                onStatusUpdate?.Invoke("正在下载插件...");

                // 下载插件
                var pluginData = await MarketApiService.DownloadPluginAsync(
                    marketPlugin.DownloadUrl,
                    progress);

                onStatusUpdate?.Invoke("正在安装插件...");

                // 保存到临时文件
                var tempFile = Path.Combine(Path.GetTempPath(), $"{marketPlugin.Id}.lpak");
                await File.WriteAllBytesAsync(tempFile, pluginData);

                // 调用 LpakInstallerService 安装
                var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                var installer = new LpakInstallerService(pluginsDir);
                var result = await installer.InstallAsync(tempFile);

                // 清理临时文件
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // 忽略清理失败
                }

                if (result.IsSuccess)
                {
                    onStatusUpdate?.Invoke($"安装成功：{marketPlugin.Name}");

                    // 触发热加载
                    await Task.Delay(500); // 等待文件系统监视器触发

                    return true;
                }
                else
                {
                    onStatusUpdate?.Invoke($"安装失败：{result.ErrorMessage}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "下载插件失败: {PluginId}", marketPlugin.Id);
                onStatusUpdate?.Invoke($"下载失败：网络错误");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "安装插件失败: {PluginId}", marketPlugin.Id);
                onStatusUpdate?.Invoke($"安装失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新插件
        /// </summary>
        public static async Task<bool> UpdatePluginAsync(
            string pluginId,
            MarketPlugin newVersion,
            IProgress<double>? progress = null,
            Action<string>? onStatusUpdate = null)
        {
            try
            {
                onStatusUpdate?.Invoke("正在准备更新...");

                // 卸载旧版本 - 通过删除插件目录
                var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                var pluginDir = Path.Combine(pluginsDir, pluginId);
                if (Directory.Exists(pluginDir))
                {
                    Directory.Delete(pluginDir, recursive: true);
                    await Task.Delay(500); // 等待文件系统监视器触发卸载
                }

                onStatusUpdate?.Invoke("正在下载新版本...");

                // 安装新版本
                return await InstallFromMarketAsync(newVersion, progress, onStatusUpdate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "更新插件失败: {PluginId}", pluginId);
                onStatusUpdate?.Invoke($"更新失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查插件是否已安装
        /// </summary>
        public static bool IsPluginInstalled(string pluginId)
        {
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            var pluginDir = Path.Combine(pluginsDir, pluginId);
            return Directory.Exists(pluginDir);
        }

        /// <summary>
        /// 获取已安装插件的版本
        /// </summary>
        public static string? GetInstalledVersion(string pluginId)
        {
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            var pluginDir = Path.Combine(pluginsDir, pluginId);
            var manifestPath = Path.Combine(pluginDir, "manifest.json");

            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(json);
                return manifest?.Version;
            }
            catch
            {
                return null;
            }
        }
    }
}
