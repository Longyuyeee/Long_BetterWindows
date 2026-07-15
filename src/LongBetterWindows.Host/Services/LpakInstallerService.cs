using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// .lpak 包安装服务
    /// </summary>
    public class LpakInstallerService
    {
        private readonly string _pluginsDir;

        public LpakInstallerService(string pluginsDir)
        {
            _pluginsDir = pluginsDir;
        }

        /// <summary>
        /// 安装 .lpak 包
        /// </summary>
        public async Task<InstallResult> InstallAsync(string lpakFilePath)
        {
            if (!File.Exists(lpakFilePath))
                return InstallResult.Failure("文件不存在");

            if (!Path.GetExtension(lpakFilePath).Equals(".lpak", StringComparison.OrdinalIgnoreCase))
                return InstallResult.Failure("不是有效的 .lpak 文件");

            string? tempDir = null;
            try
            {
                // 1. 解压到临时目录
                tempDir = Path.Combine(Path.GetTempPath(), $"lpak_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                ZipFile.ExtractToDirectory(lpakFilePath, tempDir);
                Log.Information("已解压 .lpak 到临时目录: {TempDir}", tempDir);

                // 2. 验证 manifest.json
                var manifestPath = Path.Combine(tempDir, "manifest.json");
                if (!File.Exists(manifestPath))
                    return InstallResult.Failure("缺少 manifest.json");

                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson);

                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                    return InstallResult.Failure("manifest.json 格式错误");

                // 3. 检查 ID 冲突
                var targetDir = Path.Combine(_pluginsDir, manifest.Id);
                if (Directory.Exists(targetDir))
                {
                    Log.Warning("插件 {Id} 已存在，将覆盖", manifest.Id);
                }

                // 4. 验证运行时类型
                if (manifest.Runtime != "web" && manifest.Runtime != "dll" && manifest.Runtime != "script")
                    return InstallResult.Failure($"不支持的运行时类型: {manifest.Runtime}");

                // 5. 验证入口点文件存在
                if (!string.IsNullOrEmpty(manifest.EntryPoint))
                {
                    var entryPath = Path.Combine(tempDir, manifest.EntryPoint);
                    if (!File.Exists(entryPath))
                        return InstallResult.Failure($"入口点文件不存在: {manifest.EntryPoint}");
                }

                // 6. 移动到 Plugins 目录
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }

                Directory.Move(tempDir, targetDir);
                tempDir = null; // 标记已移动，避免清理

                Log.Information("插件 {Id} 安装成功", manifest.Id);
                return InstallResult.Success(manifest);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "安装 .lpak 失败");
                return InstallResult.Failure($"安装失败: {ex.Message}");
            }
            finally
            {
                // 清理临时目录
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "清理临时目录失败: {TempDir}", tempDir);
                    }
                }
            }
        }

        /// <summary>
        /// 验证 .lpak 包但不安装（预览）
        /// </summary>
        public async Task<InstallResult> ValidateAsync(string lpakFilePath)
        {
            if (!File.Exists(lpakFilePath))
                return InstallResult.Failure("文件不存在");

            string? tempDir = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), $"lpak_validate_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                ZipFile.ExtractToDirectory(lpakFilePath, tempDir);

                var manifestPath = Path.Combine(tempDir, "manifest.json");
                if (!File.Exists(manifestPath))
                    return InstallResult.Failure("缺少 manifest.json");

                var manifestJson = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson);

                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                    return InstallResult.Failure("manifest.json 格式错误");

                return InstallResult.Success(manifest);
            }
            catch (Exception ex)
            {
                return InstallResult.Failure($"验证失败: {ex.Message}");
            }
            finally
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch { /* 忽略清理错误 */ }
                }
            }
        }
    }

    /// <summary>
    /// 安装结果
    /// </summary>
    public class InstallResult
    {
        public bool IsSuccess { get; init; }
        public string? ErrorMessage { get; init; }
        public PluginManifest? Manifest { get; init; }

        public static InstallResult Success(PluginManifest manifest)
            => new() { IsSuccess = true, Manifest = manifest };

        public static InstallResult Failure(string errorMessage)
            => new() { IsSuccess = false, ErrorMessage = errorMessage };
    }
}
