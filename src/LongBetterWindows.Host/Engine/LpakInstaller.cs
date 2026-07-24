using System.IO;
using System.IO.Compression;
using LongBetterWindows.Host.Core;
using System.Security.Cryptography;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// Long 原生插件包安装器。所有变更先进入同卷暂存区，并在扫描失败时恢复旧版本。
    /// </summary>
    public class LpakInstaller
    {
        private readonly string _pluginsDir;
        private readonly PluginScanner _scanner;
        private readonly SemaphoreSlim _transactionGate = new(1, 1);
        private PluginPackageValidator _validator;

        public LpakInstaller(
            PluginScanner scanner,
            string? pluginsDir = null,
            PluginPackageValidator? validator = null)
        {
            _scanner = scanner;
            _pluginsDir = Path.GetFullPath(pluginsDir ?? Path.Combine(AppContext.BaseDirectory, "Plugins"));
            _validator = validator ?? new PluginPackageValidator();
            Directory.CreateDirectory(_pluginsDir);
        }

        public void ConfigureTrustStore(IPublisherTrustStore trustStore)
            => _validator = new PluginPackageValidator(trustStore: trustStore);

        public async Task<InstallResult> InstallAsync(
            string lpakPath,
            MarketplacePackageMetadata? metadata = null)
        {
            await _transactionGate.WaitAsync();
            try
            {
                await RecoverInterruptedTransactionsCoreAsync();
                return await InstallCoreAsync(lpakPath, metadata);
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        private async Task<InstallResult> InstallCoreAsync(
            string lpakPath,
            MarketplacePackageMetadata? metadata)
        {
            if (!File.Exists(lpakPath))
                return InstallResult.Fail(
                    InstallErrorCode.SourceNotFound,
                    "文件不存在：" + lpakPath);
            if (!lpakPath.EndsWith(".lpak", StringComparison.OrdinalIgnoreCase))
                return InstallResult.Fail(
                    InstallErrorCode.InvalidPackageExtension,
                    "不是 .lpak 文件。");

            var validation = await _validator.ValidateAsync(lpakPath, metadata);
            if (!validation.IsSuccess)
                return InstallResult.Fail(
                    InstallErrorCode.PackageValidationFailed,
                    validation.Error ?? "插件包校验失败。",
                    validation);

            var manifest = validation.Manifest!;
            var targetDir = GetPluginDirectory(manifest.Id);
            PluginManifest? installedManifest = null;
            if (Directory.Exists(targetDir))
            {
                var existing = await ManifestReader.ReadAsync(targetDir);
                installedManifest = existing.Manifest;
            }

            var permissionDiff = PluginPackageValidator.CreatePermissionDiff(
                installedManifest?.Capabilities,
                manifest.Capabilities);
            var transactionId = Guid.NewGuid().ToString("N");
            var parentDir = Directory.GetParent(_pluginsDir)?.FullName ?? _pluginsDir;
            var transactionDir = Path.Combine(parentDir, $".long-transaction-{transactionId}");
            var stagingDir = Path.Combine(transactionDir, "staging");
            var backupDir = Path.Combine(transactionDir, "backup");
            var targetMovedToBackup = false;
            var stagedMovedToTarget = false;
            var preserveBackup = false;

            Log.Information("开始插件安装事务: {PluginId} v{Version}", manifest.Id, manifest.Version);
            try
            {
                Directory.CreateDirectory(transactionDir);
                await WriteJournalAsync(transactionDir, manifest.Id, TransactionPhase.Prepared);
                Directory.CreateDirectory(stagingDir);
                await EnsurePackageUnchangedAsync(lpakPath, validation.Sha256!);
                using (var archive = ZipFile.OpenRead(lpakPath))
                    PluginPackageValidator.ExtractSafely(archive, stagingDir);

                // 暂存内容在移动前再读一次，避免验证与落盘使用两套 Manifest。
                var stagedManifest = await ManifestReader.ReadAsync(stagingDir);
                if (!stagedManifest.IsSuccess
                    || !string.Equals(stagedManifest.Manifest!.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(stagedManifest.Manifest.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("暂存包 Manifest 与已验证内容不一致。");

                if (Directory.Exists(targetDir))
                {
                    await UnloadPluginAsync(manifest.Id);
                    Log.Debug("插件旧版本运行时已释放: {PluginId}", manifest.Id);
                    await MoveDirectoryWithRetryAsync(targetDir, backupDir);
                    targetMovedToBackup = true;
                    Log.Debug("插件旧版本已移动到事务备份: {PluginId}", manifest.Id);
                    await WriteJournalAsync(transactionDir, manifest.Id, TransactionPhase.BackedUp);
                }

                await MoveDirectoryWithRetryAsync(stagingDir, targetDir);
                stagedMovedToTarget = true;
                Log.Debug("插件新版本已从暂存区提交到目标目录: {PluginId}", manifest.Id);
                await _scanner.ReloadPluginDirectoryAsync(targetDir);
                Log.Debug("插件新版本运行时已重新加载: {PluginId}", manifest.Id);
                await WriteJournalAsync(transactionDir, manifest.Id, TransactionPhase.Committed);
                Directory.Delete(transactionDir, true);

                Log.Information("插件安装事务完成: {PluginId} v{Version}", manifest.Id, manifest.Version);
                return InstallResult.Ok(
                    manifest.Name,
                    manifest.Id,
                    manifest.Version,
                    installedManifest == null ? InstallAction.Install : InstallAction.Replace,
                    validation,
                    permissionDiff);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "插件安装事务失败，正在回滚: {PluginId}", manifest.Id);
                try
                {
                    if (stagedMovedToTarget && Directory.Exists(targetDir))
                        Directory.Delete(targetDir, true);
                    if (targetMovedToBackup && Directory.Exists(backupDir))
                        await MoveDirectoryWithRetryAsync(backupDir, targetDir);
                    if (targetMovedToBackup)
                        await _scanner.ReloadPluginDirectoryAsync(targetDir);
                }
                catch (Exception rollbackError)
                {
                    preserveBackup = Directory.Exists(backupDir);
                    Log.Error(rollbackError, "插件回滚失败: {PluginId}", manifest.Id);
                    return InstallResult.Fail(
                        InstallErrorCode.InstallRollbackFailed,
                        $"安装失败且回滚失败：{ex.Message}；{rollbackError.Message}",
                        validation);
                }
                return InstallResult.Fail(
                    InstallErrorCode.InstallFailedRolledBack,
                    $"安装失败，已恢复旧版本：{ex.Message}",
                    validation);
            }
            finally
            {
                if (!preserveBackup) TryDelete(transactionDir);
            }
        }

        public async Task<InstallResult> UninstallAsync(string pluginId)
        {
            await _transactionGate.WaitAsync();
            try
            {
                await RecoverInterruptedTransactionsCoreAsync();
                return await UninstallCoreAsync(pluginId);
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        private async Task<InstallResult> UninstallCoreAsync(string pluginId)
        {
            var targetDir = GetPluginDirectory(pluginId);
            if (!Directory.Exists(targetDir))
                return InstallResult.Fail(
                    InstallErrorCode.PluginNotInstalled,
                    "插件未安装。");

            var existing = await ManifestReader.ReadAsync(targetDir);
            if (!existing.IsSuccess)
                return InstallResult.Fail(
                    InstallErrorCode.InstalledManifestInvalid,
                    $"已安装插件 Manifest 无效：{existing.Error}",
                    manifestFailureCode: existing.ErrorCode);

            var parentDir = Directory.GetParent(_pluginsDir)?.FullName ?? _pluginsDir;
            var transactionDir = Path.Combine(parentDir, $".long-transaction-{Guid.NewGuid():N}");
            var backupDir = Path.Combine(transactionDir, "backup");
            var preserveBackup = false;
            try
            {
                Directory.CreateDirectory(transactionDir);
                await WriteJournalAsync(transactionDir, pluginId, TransactionPhase.Prepared);
                await UnloadPluginAsync(pluginId);
                await MoveDirectoryWithRetryAsync(targetDir, backupDir);
                await WriteJournalAsync(transactionDir, pluginId, TransactionPhase.BackedUp);
                await _scanner.ReloadPluginDirectoryAsync(targetDir);
                await WriteJournalAsync(transactionDir, pluginId, TransactionPhase.Committed);
                Directory.Delete(transactionDir, true);
                return InstallResult.Ok(
                    existing.Manifest!.Name,
                    pluginId,
                    existing.Manifest.Version,
                    InstallAction.Uninstall,
                    null,
                    new PermissionDiff { Removed = existing.Manifest.Capabilities.OrderBy(x => x).ToArray() });
            }
            catch (Exception ex)
            {
                try
                {
                    if (!Directory.Exists(targetDir) && Directory.Exists(backupDir))
                        await MoveDirectoryWithRetryAsync(backupDir, targetDir);
                    await _scanner.ReloadPluginDirectoryAsync(targetDir);
                }
                catch (Exception rollbackError)
                {
                    preserveBackup = Directory.Exists(backupDir);
                    return InstallResult.Fail(
                        InstallErrorCode.UninstallRollbackFailed,
                        $"卸载失败且回滚失败：{ex.Message}；{rollbackError.Message}");
                }
                return InstallResult.Fail(
                    InstallErrorCode.UninstallFailedRolledBack,
                    $"卸载失败，已恢复插件：{ex.Message}");
            }
            finally
            {
                if (!preserveBackup) TryDelete(transactionDir);
            }
        }

        public async Task<int> RecoverInterruptedTransactionsAsync()
        {
            await _transactionGate.WaitAsync();
            try
            {
                return await RecoverInterruptedTransactionsCoreAsync();
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        private async Task<int> RecoverInterruptedTransactionsCoreAsync()
        {
            var parentDir = Directory.GetParent(_pluginsDir)?.FullName ?? _pluginsDir;
            if (!Directory.Exists(parentDir)) return 0;

            var recovered = 0;
            foreach (var transactionDir in Directory.GetDirectories(parentDir, ".long-transaction-*"))
            {
                var journalPath = Path.Combine(transactionDir, "journal.json");
                if (!File.Exists(journalPath))
                {
                    Log.Warning("发现无事务日志的插件临时目录，保留供人工检查: {Directory}", transactionDir);
                    continue;
                }

                try
                {
                    var journal = JsonSerializer.Deserialize<InstallTransactionJournal>(
                        await File.ReadAllTextAsync(journalPath));
                    if (journal == null || string.IsNullOrWhiteSpace(journal.PluginId))
                        throw new InvalidDataException("插件事务日志无效。");

                    var targetDir = GetPluginDirectory(journal.PluginId);
                    var backupDir = Path.Combine(transactionDir, "backup");
                    if (journal.Phase != TransactionPhase.Committed)
                    {
                        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                        if (Directory.Exists(backupDir))
                            await MoveDirectoryWithRetryAsync(backupDir, targetDir);
                        Log.Warning("已恢复未提交的插件事务: {PluginId}", journal.PluginId);
                    }

                    Directory.Delete(transactionDir, true);
                    recovered++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "插件事务自动恢复失败，保留现场: {Directory}", transactionDir);
                }
            }

            return recovered;
        }

        public async Task<int> InstallAllFromDirectoryAsync(string? sourceDir = null)
        {
            sourceDir ??= _pluginsDir;
            if (!Directory.Exists(sourceDir)) return 0;

            var count = 0;
            foreach (var file in Directory.GetFiles(sourceDir, "*.lpak"))
            {
                var result = await InstallAsync(file);
                if (result.IsSuccess)
                {
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

        private string GetPluginDirectory(string pluginId)
        {
            var target = Path.GetFullPath(Path.Combine(_pluginsDir, Sanitize(pluginId)));
            var root = _pluginsDir + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件 ID 生成了非法安装路径。");
            return target;
        }

        private static async Task MoveDirectoryWithRetryAsync(
            string source,
            string destination)
        {
            const int maximumAttempts = 6;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException
                    && attempt < maximumAttempts - 1
                    && Directory.Exists(source)
                    && !Directory.Exists(destination))
                {
                    await Task.Delay(25 << attempt);
                }
            }
        }

        private async Task UnloadPluginAsync(string pluginId)
        {
            if (await _scanner.UnloadPluginAsync(pluginId)) return;

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

        private static async Task EnsurePackageUnchangedAsync(string path, string expectedSha256)
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expectedSha256)))
                throw new InvalidDataException("插件包在校验后发生变化。");
        }

        private static async Task WriteJournalAsync(
            string transactionDir, string pluginId, TransactionPhase phase)
        {
            var path = Path.Combine(transactionDir, "journal.json");
            var temporary = path + ".tmp";
            var json = JsonSerializer.Serialize(new InstallTransactionJournal
            {
                PluginId = pluginId,
                Phase = phase,
            });
            Log.Debug(
                "正在写入插件事务日志: {PluginId}; Phase={Phase}",
                pluginId,
                phase);
            await File.WriteAllTextAsync(temporary, json);
            Log.Debug(
                "插件事务临时日志已落盘: {PluginId}; Phase={Phase}",
                pluginId,
                phase);
            File.Move(temporary, path, true);
            Log.Debug(
                "插件事务日志已提交: {PluginId}; Phase={Phase}",
                pluginId,
                phase);
        }

        private static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        private static string Sanitize(string id)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = id.Select(c => invalid.Contains(c) || c is '.' or '/' or '\\' ? '-' : c).ToArray();
            return new string(chars).Trim('-', ' ');
        }

        private sealed class InstallTransactionJournal
        {
            public string PluginId { get; init; } = string.Empty;
            public TransactionPhase Phase { get; init; }
        }

        private enum TransactionPhase
        {
            Prepared,
            BackedUp,
            Committed,
        }
    }

}
