using System.Diagnostics;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Interaction;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public enum PluginShutdownStatus
    {
        Passed,
        Failed,
        TimedOut,
        SkippedTotalBudget,
    }

    public sealed record PluginShutdownEntryResult(
        string PluginId,
        PluginShutdownStatus Status,
        double ElapsedMilliseconds,
        double? WaitBudgetMilliseconds);

    public sealed record PluginShutdownReport(
        double ElapsedMilliseconds,
        double? TotalBudgetMilliseconds,
        IReadOnlyList<PluginShutdownEntryResult> Results)
    {
        public bool Completed => Results.All(result =>
            result.Status == PluginShutdownStatus.Passed);

        public IReadOnlyList<string> IncompletePluginIds => Results
            .Where(result => result.Status != PluginShutdownStatus.Passed)
            .Select(result => result.PluginId)
            .ToArray();
    }

    public sealed class IncompletePluginShutdownException : Exception
    {
        public IncompletePluginShutdownException(
            IReadOnlyList<string> incompletePluginIds)
            : base("One or more plugins did not complete host shutdown.")
        {
            IncompletePluginIds = incompletePluginIds
                ?? throw new ArgumentNullException(nameof(incompletePluginIds));
        }

        public IReadOnlyList<string> IncompletePluginIds { get; }
    }

    public class PluginRegistry
    {
        private readonly Dictionary<string, PluginEntry> _entries = new();
        private readonly object _lock = new();
        private SearchCoordinator? _searchCoordinator;
        private Func<string, Task>? _hostResourceReleaser;
        private long _catalogRevision;
        private int _changeBatchDepth;
        private bool _changePending;

        /// <summary>随插件注册和注销自动同步的功能指令索引。</summary>
        public CommandRegistry Commands { get; } = new();

        /// <summary>进程内、无敏感内容的插件命令健康度聚合。</summary>
        public PluginRuntimeHealthMonitor RuntimeHealth { get; } = new();

        /// <summary>插件注册/注销/状态变化时触发（在 UI 线程订阅后需自行 Dispatch）</summary>
        public event Action? PluginsChanged;

        internal IDisposable BeginChangeBatch()
        {
            lock (_lock)
                _changeBatchDepth++;
            return new ChangeBatch(this);
        }

        public void AttachSearchCoordinator(SearchCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            lock (_lock)
            {
                _searchCoordinator = coordinator;
                foreach (var entry in _entries.Values)
                    SyncSearchProvider(entry);
            }
        }

        public void AttachHostResourceReleaser(Func<string, Task> releaser)
            => _hostResourceReleaser = releaser
                ?? throw new ArgumentNullException(nameof(releaser));

        internal Task ReleaseHostResourcesAsync(string pluginId)
            => _hostResourceReleaser?.Invoke(pluginId) ?? Task.CompletedTask;

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>插件命令目录注册或注销时递增；运行状态变化不影响该值。</summary>
        public long CatalogRevision
        {
            get { lock (_lock) return _catalogRevision; }
        }

        public PluginEntry? Get(string pluginId)
        {
            lock (_lock)
            {
                _entries.TryGetValue(pluginId, out var entry);
                return entry;
            }
        }

        public IReadOnlyList<PluginEntry> GetAll()
        {
            lock (_lock)
            {
                return _entries.Values.ToList();
            }
        }

        public bool Register(PluginManifest manifest, object instance, PluginLoadContext? context, string directory)
        {
            lock (_lock)
            {
                if (_entries.ContainsKey(manifest.Id))
                {
                    Log.Warning("插件 {PluginId} 已注册，跳过重复注册", manifest.Id);
                    return false;
                }

                _catalogRevision++;
                var entry = new PluginEntry(manifest, instance, directory, _catalogRevision)
                {
                    State = PluginState.Loaded
                };

                _entries[manifest.Id] = entry;
                Commands.RegisterManifest(manifest);
                Log.Information("插件 {PluginId} (v{Version}) 已注册", manifest.Id, manifest.Version);
            }

            RuntimeHealth.RecordLifecycleTransition(
                manifest.Id,
                PluginRuntimeLifecycleState.Loaded);
            NotifyChanged();
            return true;
        }

        public bool Unregister(string pluginId)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(pluginId, out var entry))
                    return false;

                entry.State = PluginState.Stopped;
                _searchCoordinator?.UnregisterProvider("plugin:" + pluginId);
                _entries.Remove(pluginId);
                Commands.UnregisterPlugin(pluginId);
                _catalogRevision++;
                Log.Information("插件 {PluginId} 已注销", pluginId);
            }

            RuntimeHealth.RecordLifecycleTransition(
                pluginId,
                PluginRuntimeLifecycleState.Unloaded);
            NotifyChanged();
            return true;
        }

        public bool SetState(string pluginId, PluginState state)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(pluginId, out var entry))
                    return false;

                if (!CanTransition(entry.State, state))
                {
                    Log.Warning(
                        "插件 {PluginId} 生命周期转换被拒绝: {FromState} -> {ToState}",
                        pluginId,
                        entry.State,
                        state);
                    return false;
                }

                entry.State = state;
                SyncSearchProvider(entry);
            }

            RuntimeHealth.RecordLifecycleTransition(pluginId, state switch
            {
                PluginState.Loaded => PluginRuntimeLifecycleState.Loaded,
                PluginState.Running => PluginRuntimeLifecycleState.Running,
                PluginState.Background => PluginRuntimeLifecycleState.Background,
                PluginState.Stopped => PluginRuntimeLifecycleState.Stopped,
                PluginState.Error => PluginRuntimeLifecycleState.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(state)),
            });
            NotifyChanged();
            return true;
        }

        internal bool RegisterDeferred(
            PluginManifest manifest,
            string directory,
            Func<PluginEntry, Task<object?>> activator)
        {
            lock (_lock)
            {
                if (_entries.ContainsKey(manifest.Id))
                    return false;

                _catalogRevision++;
                var entry = new PluginEntry(
                    manifest,
                    directory,
                    _catalogRevision,
                    activator)
                {
                    State = PluginState.Loaded,
                };
                _entries[manifest.Id] = entry;
                Commands.RegisterManifest(manifest);
                Log.Information(
                    "插件 {PluginId} (v{Version}) 已延迟注册",
                    manifest.Id,
                    manifest.Version);
            }

            RuntimeHealth.RecordLifecycleTransition(
                manifest.Id,
                PluginRuntimeLifecycleState.Loaded);
            NotifyChanged();
            return true;
        }

        internal bool ApplyLocalization(
            string pluginId,
            PluginLanguageContext context)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(pluginId, out var entry))
                    return false;
                entry.ApplyLanguageContext(context);
                Commands.ApplyLocalization(pluginId, context);
            }

            NotifyChanged();
            return true;
        }

        public bool HasCapability(string pluginId, string capability)
        {
            var entry = Get(pluginId);
            return entry != null && entry.HasCapability(capability);
        }

        private void NotifyChanged()
        {
            Action? changed;
            lock (_lock)
            {
                if (_changeBatchDepth > 0)
                {
                    _changePending = true;
                    return;
                }

                changed = PluginsChanged;
            }
            changed?.Invoke();
        }

        private void EndChangeBatch()
        {
            Action? changed = null;
            lock (_lock)
            {
                if (_changeBatchDepth <= 0)
                    throw new InvalidOperationException(
                        "Plugin registry change batch is not active.");

                _changeBatchDepth--;
                if (_changeBatchDepth == 0 && _changePending)
                {
                    _changePending = false;
                    changed = PluginsChanged;
                }
            }
            changed?.Invoke();
        }

        private void SyncSearchProvider(PluginEntry entry)
        {
            if (_searchCoordinator is null
                || entry.Instance is not IPluginSearchProvider provider)
                return;

            var providerId = "plugin:" + entry.Id;
            var searchAvailable = entry.State == PluginState.Running
                || entry.State == PluginState.Background && entry.Lifecycle.SearchInBackground;
            if (searchAvailable)
            {
                _searchCoordinator.RegisterProvider(
                    new PluginSearchProviderAdapter(
                        entry.Id,
                        provider,
                        InstalledPluginRailProjection.FindIconPath(
                            entry.Directory)));
            }
            else
            {
                _searchCoordinator.UnregisterProvider(providerId);
            }
        }

        public IReadOnlyList<string> GetPluginCapabilities(string pluginId)
        {
            var entry = Get(pluginId);
            return entry?.Manifest.Capabilities.AsReadOnly()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        public async Task<bool> StartPluginAsync(
            string pluginId,
            bool persistAutoStart = true)
        {
            PluginEntry? entry;
            lock (_lock)
            {
                entry = Get(pluginId);
            }

            if (entry == null) return false;

            await entry.LifecycleGate.WaitAsync();
            try
            {
                if (entry.State == PluginState.Running)
                    return false;
                if (!await entry.EnsureActivatedAsync())
                {
                    RuntimeHealth.RecordLifecycleFailure(
                        pluginId,
                        PluginRuntimeFailureKind.ActivationFailed);
                    Log.Error("插件 {PluginId} 运行时激活失败", pluginId);
                    SetState(pluginId, PluginState.Error);
                    return false;
                }

                using (PluginAccessContext.Enter(pluginId))
                {
                    if (entry.Instance is ILongPlugin plugin)
                    {
                        var ok = entry.State == PluginState.Background
                            ? plugin is IPluginBackgroundLifecycle background
                                ? await background.ResumeAsync()
                                : true
                            : await plugin.StartAsync();
                        if (ok)
                        {
                            SetState(pluginId, PluginState.Running);
                            if (persistAutoStart)
                                entry.SetAutoStart(true);
                            Log.Information("插件 {PluginId} 已启用", pluginId);
                        }
                        else
                        {
                            RuntimeHealth.RecordLifecycleFailure(
                                pluginId,
                                PluginRuntimeFailureKind.StartFailed);
                        }
                        return ok;
                    }
                    else
                    {
                        // WebPluginRuntime 等非 ILongPlugin 类型，默认已运行
                        SetState(pluginId, PluginState.Running);
                        if (persistAutoStart)
                            entry.SetAutoStart(true);
                        Log.Information("插件 {PluginId} 已启用", pluginId);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                RuntimeHealth.RecordLifecycleFailure(
                    pluginId,
                    PluginRuntimeFailureKind.StartFailed,
                    isException: true);
                Log.Error(ex, "插件 {PluginId} 启动失败", pluginId);
                SetState(pluginId, PluginState.Error);
                return false;
            }
            finally
            {
                entry.LifecycleGate.Release();
            }
        }

        public async Task<bool> StopPluginAsync(
            string pluginId,
            bool persistAutoStart = true)
        {
            PluginEntry? entry;
            lock (_lock)
            {
                entry = Get(pluginId);
            }

            if (entry == null)
                return false;

            await entry.LifecycleGate.WaitAsync();
            try
            {
                if (entry.State is not (PluginState.Running or PluginState.Background))
                    return false;
                using (PluginAccessContext.Enter(pluginId))
                {
                    if (entry.Instance is ILongPlugin plugin)
                    {
                        if (!await plugin.StopAsync())
                        {
                            RuntimeHealth.RecordLifecycleFailure(
                                pluginId,
                                PluginRuntimeFailureKind.StopFailed);
                            Log.Warning("插件 {PluginId} 拒绝停止", pluginId);
                            return false;
                        }
                    }
                    if (entry.Instance is IPluginResourceLifecycle resources)
                    {
                        try
                        {
                            await resources.ReleaseResourcesAsync();
                        }
                        catch (Exception ex)
                        {
                            RuntimeHealth.RecordLifecycleFailure(
                                pluginId,
                                PluginRuntimeFailureKind.ResourceReleaseFailed,
                                isException: true);
                            Log.Error(ex, "插件 {PluginId} 资源释放失败", pluginId);
                            return false;
                        }
                    }
                }
                try
                {
                    await ReleaseHostResourcesAsync(pluginId);
                }
                catch (Exception ex)
                {
                    RuntimeHealth.RecordLifecycleFailure(
                        pluginId,
                        PluginRuntimeFailureKind.ResourceReleaseFailed,
                        isException: true);
                    Log.Error(ex, "插件 {PluginId} 宿主资源释放失败", pluginId);
                    return false;
                }
                SetState(pluginId, PluginState.Stopped);
                if (persistAutoStart)
                    entry.SetAutoStart(false);
                Log.Information("插件 {PluginId} 已停止", pluginId);
                return true;
            }
            catch (Exception ex)
            {
                RuntimeHealth.RecordLifecycleFailure(
                    pluginId,
                    PluginRuntimeFailureKind.StopFailed,
                    isException: true);
                Log.Error(ex, "插件 {PluginId} 停止失败", pluginId);
                return false;
            }
            finally
            {
                entry.LifecycleGate.Release();
            }
        }

        public async Task<bool> MoveToBackgroundAsync(string pluginId)
        {
            var entry = Get(pluginId);
            if (entry == null || entry.State != PluginState.Running
                || entry.Lifecycle.CloseBehavior != PluginCloseBehavior.Background)
                return false;

            try
            {
                using (PluginAccessContext.Enter(pluginId))
                {
                    if (entry.Instance is IPluginBackgroundLifecycle background
                        && !await background.EnterBackgroundAsync())
                    {
                        RuntimeHealth.RecordLifecycleFailure(
                            pluginId,
                            PluginRuntimeFailureKind.BackgroundTransitionFailed);
                        return false;
                    }
                }

                if (!SetState(pluginId, PluginState.Background))
                    return false;

                Log.Information("插件 {PluginId} 已转入后台", pluginId);
                return true;
            }
            catch (Exception ex)
            {
                RuntimeHealth.RecordLifecycleFailure(
                    pluginId,
                    PluginRuntimeFailureKind.BackgroundTransitionFailed,
                    isException: true);
                Log.Error(ex, "插件 {PluginId} 转入后台失败", pluginId);
                SetState(pluginId, PluginState.Error);
                return false;
            }
        }

        public Task<bool> HandleWindowClosedAsync(string pluginId)
        {
            var entry = Get(pluginId);
            if (entry == null) return Task.FromResult(false);
            return entry.Lifecycle.CloseBehavior == PluginCloseBehavior.Background
                ? MoveToBackgroundAsync(pluginId)
                : StopPluginAsync(pluginId, persistAutoStart: false);
        }

        public async Task<PluginShutdownReport> ShutdownAllAsync(
            TimeSpan? perPluginTimeout = null,
            TimeSpan? totalTimeout = null)
        {
            if (perPluginTimeout is { } timeout && timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(perPluginTimeout));
            if (totalTimeout is { } totalBudget && totalBudget <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(totalTimeout));

            var entries = GetAll();
            var results = new List<PluginShutdownEntryResult>(entries.Count);
            var totalStopwatch = Stopwatch.StartNew();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var remainingTotalBudget = totalTimeout - totalStopwatch.Elapsed;
                if (remainingTotalBudget is { } remaining && remaining <= TimeSpan.Zero)
                {
                    AddBudgetSkippedResults(entries, index, results);
                    break;
                }

                var waitBudget = SelectWaitBudget(
                    perPluginTimeout,
                    remainingTotalBudget);
                var limitedByTotalBudget = remainingTotalBudget is not null
                    && (perPluginTimeout is null
                        || remainingTotalBudget <= perPluginTimeout);
                var cleanup = ShutdownEntryAsync(entry);
                var entryStopwatch = Stopwatch.StartNew();
                try
                {
                    if (waitBudget is { } budget)
                        await cleanup.WaitAsync(budget);
                    else
                        await cleanup;
                    results.Add(new PluginShutdownEntryResult(
                        entry.Id,
                        PluginShutdownStatus.Passed,
                        entryStopwatch.Elapsed.TotalMilliseconds,
                        waitBudget?.TotalMilliseconds));
                }
                catch (TimeoutException ex)
                {
                    RuntimeHealth.RecordLifecycleFailure(
                        entry.Id,
                        PluginRuntimeFailureKind.ShutdownTimeout,
                        isException: true);
                    Log.Error(
                        ex,
                        "Plugin {PluginId} shutdown exceeded {TimeoutMs} ms; continuing host cleanup",
                        entry.Id,
                        waitBudget!.Value.TotalMilliseconds);
                    results.Add(new PluginShutdownEntryResult(
                        entry.Id,
                        PluginShutdownStatus.TimedOut,
                        entryStopwatch.Elapsed.TotalMilliseconds,
                        waitBudget.Value.TotalMilliseconds));
                    _ = ObserveLateShutdownAsync(cleanup, entry.Id);
                    if (limitedByTotalBudget)
                    {
                        AddBudgetSkippedResults(entries, index + 1, results);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "插件 {PluginId} 退出释放失败", entry.Id);
                    results.Add(new PluginShutdownEntryResult(
                        entry.Id,
                        PluginShutdownStatus.Failed,
                        entryStopwatch.Elapsed.TotalMilliseconds,
                        waitBudget?.TotalMilliseconds));
                }
            }

            var report = new PluginShutdownReport(
                totalStopwatch.Elapsed.TotalMilliseconds,
                totalTimeout?.TotalMilliseconds,
                results);
            if (!report.Completed)
            {
                Log.Warning(
                    "Plugin shutdown completed with incomplete plugins after {ElapsedMs} ms: {@PluginIds}",
                    report.ElapsedMilliseconds,
                    report.IncompletePluginIds);
            }
            return report;
        }

        public async ValueTask ShutdownAllForHostAsync(
            TimeSpan perPluginTimeout,
            TimeSpan totalTimeout)
        {
            var report = await ShutdownAllAsync(
                perPluginTimeout,
                totalTimeout).ConfigureAwait(false);
            if (!report.Completed)
                throw new IncompletePluginShutdownException(
                    report.IncompletePluginIds);
        }

        private static TimeSpan? SelectWaitBudget(
            TimeSpan? perPluginTimeout,
            TimeSpan? remainingTotalBudget)
        {
            if (perPluginTimeout is null)
                return remainingTotalBudget;
            if (remainingTotalBudget is null)
                return perPluginTimeout;
            return perPluginTimeout <= remainingTotalBudget
                ? perPluginTimeout
                : remainingTotalBudget;
        }

        private static void AddBudgetSkippedResults(
            IReadOnlyList<PluginEntry> entries,
            int startIndex,
            ICollection<PluginShutdownEntryResult> results)
        {
            for (var index = startIndex; index < entries.Count; index++)
            {
                results.Add(new PluginShutdownEntryResult(
                    entries[index].Id,
                    PluginShutdownStatus.SkippedTotalBudget,
                    0,
                    0));
            }
        }

        private static async Task ObserveLateShutdownAsync(
            Task cleanup,
            string pluginId)
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Plugin {PluginId} failed after its shutdown timeout",
                    pluginId);
            }
        }

        private async Task ShutdownEntryAsync(PluginEntry entry)
        {
            await entry.LifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await ReleaseHostResourcesAsync(entry.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "Plugin {PluginId} host resource release failed during shutdown",
                        entry.Id);
                }
                using (PluginAccessContext.Enter(entry.Id))
                {
                    if (entry.State is PluginState.Running or PluginState.Background
                        && entry.Instance is ILongPlugin plugin)
                    {
                        if (!await plugin.StopAsync().ConfigureAwait(false))
                        {
                            Log.Warning(
                                "Plugin {PluginId} refused to stop during host shutdown",
                                entry.Id);
                        }
                    }
                    if (entry.Instance is IPluginResourceLifecycle resources)
                        await resources.ReleaseResourcesAsync().ConfigureAwait(false);
                }
                if (entry.Instance is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (entry.Instance is IDisposable disposable)
                    disposable.Dispose();
            }
            finally
            {
                entry.LifecycleGate.Release();
            }
        }

        public static bool CanTransition(PluginState from, PluginState to)
        {
            if (from == to) return true;
            return from switch
            {
                PluginState.Loaded => to is PluginState.Running or PluginState.Stopped or PluginState.Error,
                PluginState.Running => to is PluginState.Background or PluginState.Stopped or PluginState.Error,
                PluginState.Background => to is PluginState.Running or PluginState.Stopped or PluginState.Error,
                PluginState.Stopped => to is PluginState.Running or PluginState.Error,
                PluginState.Error => to is PluginState.Running or PluginState.Stopped,
                _ => false,
            };
        }

        public static string? GetPluginHotkey(PluginEntry entry)
        {
            var settings = entry.Manifest.DefaultSettings;
            if (settings == null) return null;

            foreach (var key in new[] { "hotkey", "record_hotkey", "play_once_hotkey" })
            {
                if (settings.TryGetValue(key, out var val) && val is JsonElement el)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }

            return null;
        }

        private sealed class ChangeBatch : IDisposable
        {
            private PluginRegistry? _owner;

            public ChangeBatch(PluginRegistry owner)
                => _owner = owner;

            public void Dispose()
                => Interlocked.Exchange(ref _owner, null)?.EndChangeBatch();
        }
    }
}
