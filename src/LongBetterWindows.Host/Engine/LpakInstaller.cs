using System.IO;
using System.IO.Compression;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// .lpak 即插即用安装器。
    /// 处理 ZIP 格式的插件包：验证 → 解压 → 加载。
    /// </summary>
    public class LpakInstaller
    {
        private readonly string _pluginsDir;
        private readonly PluginScanner _scanner;

        public LpakInstaller(PluginScanner scanner, string? pluginsDir = null)
        {
            _scanner = scanner;
            _pluginsDir = pluginsDir ?? Path.Combine(
                AppContext.BaseDirectory, "Plugins");

            if (!Directory.Exists(_pluginsDir))
                Directory.CreateDirectory(_pluginsDir);
        }

        /// <summary>
        /// 安装一个 .lpak 文件。
        /// 验证 manifest → 解压到 Plugins/ → 触发扫描加载。
        /// 如果目标已存在同 ID 插件，先卸载旧版再安装新版。
        /// </summary>
        public async Task<InstallResult> InstallAsync(string lpakPath)
        {
            if (!File.Exists(lpakPath))
                return InstallResult.Fail("文件不存在: " + lpakPath);

            if (!lpakPath.EndsWith(".lpak", StringComparison.OrdinalIgnoreCase))
                return InstallResult.Fail("不是 .lpak 文件");

            Log.Information("安装插件包: {Path}", lpakPath);

            string tempDir;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(),
                    $"lpak_{Guid.NewGuid():N}");
                ZipFile.ExtractToDirectory(lpakPath, tempDir);
            }
            catch (Exception ex)
            {
                return InstallResult.Fail($"解压失败: {ex.Message}");
            }

            try
            {
                // 验证 + 读取 manifest
                var manifestResult = await ManifestReader.ReadAsync(tempDir);
                if (!manifestResult.IsSuccess)
                {
                    return InstallResult.Fail(
                        $"manifest.json 无效: {manifestResult.Error}");
                }

                var manifest = manifestResult.Manifest!;
                var targetDir = Path.Combine(_pluginsDir, Sanitize(manifest.Id));

                // 卸载旧版本
                if (Directory.Exists(targetDir))
                {
                    Log.Information("卸载旧版插件: {PluginId}", manifest.Id);
                    await UnloadPluginAsync(manifest.Id);
                    Directory.Delete(targetDir, recursive: true);
                }

                // 移动解压内容到 Plugins/
                Directory.Move(tempDir, targetDir);

                // 触发扫描加载
                await _scanner.ScanAsync();

                Log.Information("插件安装完成: {Name} ({Id} v{Version})",
                    manifest.Name, manifest.Id, manifest.Version);

                return InstallResult.Ok(manifest.Name, manifest.Id, manifest.Version);
            }
            catch (Exception ex)
            {
                // 清理临时目录
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return InstallResult.Fail($"安装失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描 Plugins/ 目录中所有 .lpak 文件并安装。
        /// 用于启动时自动安装未解压的插件包。
        /// </summary>
        public async Task<int> InstallAllFromDirectoryAsync(string? sourceDir = null)
        {
            sourceDir ??= _pluginsDir;
            var count = 0;

            if (!Directory.Exists(sourceDir)) return 0;

            foreach (var file in Directory.GetFiles(sourceDir, "*.lpak"))
            {
                var result = await InstallAsync(file);
                if (result.IsSuccess)
                {
                    // 安装成功后删除 .lpak 源文件（内容已解压）
                    try { File.Delete(file); } catch { }
                    count++;
                }
                else
                {
                    Log.Warning("安装 {File} 失败: {Error}", file, result.Error);
                }
            }

            return count;
        }

        private async Task UnloadPluginAsync(string pluginId)
        {
            var registry = HostProvider.Instance.PluginStore;
            var entry = registry.Get(pluginId);
            if (entry == null) return;

            using (PluginAccessContext.Enter(pluginId))
            {
                try
                {
                    if (entry.Instance is ILongPlugin plugin)
                    {
                        var stopTask = plugin.StopAsync();
                        // 最多等待 1 秒，避免死锁
                        if (await Task.WhenAny(stopTask, Task.Delay(1000)) != stopTask)
                            Log.Warning("插件 {PluginId} 停止超时", pluginId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "插件 {PluginId} 停止异常", pluginId);
                }
            }

            registry.Unregister(pluginId);
        }

        private static string Sanitize(string id)
        {
            return id.Replace('.', '-').Replace('\\', '-').Replace('/', '-');
        }
    }

    public class InstallResult
    {
        public bool IsSuccess { get; init; }
        public string? PluginName { get; init; }
        public string? PluginId { get; init; }
        public string? PluginVersion { get; init; }
        public string? Error { get; init; }

        public static InstallResult Ok(string name, string id, string version)
            => new() { IsSuccess = true, PluginName = name, PluginId = id, PluginVersion = version };

        public static InstallResult Fail(string error)
            => new() { IsSuccess = false, Error = error };
    }
}
