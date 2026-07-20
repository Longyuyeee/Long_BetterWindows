using System.IO;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed record PluginFileChange(string? OldPath, string? NewPath)
    {
        public static PluginFileChange Changed(string path)
            => new(null, Path.GetFullPath(path));

        public static PluginFileChange Renamed(string? oldPath, string? newPath)
            => new(
                oldPath is null ? null : Path.GetFullPath(oldPath),
                newPath is null ? null : Path.GetFullPath(newPath));
    }

    internal sealed class PluginChangeMonitor : IDisposable
    {
        private readonly IReadOnlyList<string> _directories;
        private readonly Func<PluginFileChange, Task> _onChange;
        private readonly TimeSpan _debounceDelay;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Dictionary<string, CancellationTokenSource> _pending =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private bool _started;
        private bool _disposed;

        public PluginChangeMonitor(
            IReadOnlyList<string> directories,
            Func<PluginFileChange, Task> onChange,
            TimeSpan? debounceDelay = null)
        {
            _directories = directories;
            _onChange = onChange;
            _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(1);
        }

        public void Start()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started)
                    return;

                foreach (var directory in _directories)
                {
                    if (!Directory.Exists(directory))
                        continue;

                    var watcher = new FileSystemWatcher(directory)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = true,
                    };
                    watcher.Changed += OnFileChanged;
                    watcher.Created += OnFileChanged;
                    watcher.Deleted += OnFileChanged;
                    watcher.Renamed += OnFileRenamed;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                    Log.Debug("文件监控已启动: {Dir}", directory);
                }

                _started = true;
            }
        }

        internal void NotifyChanged(string path)
        {
            if (!PluginSourceDiscovery.IsPluginFile(path))
                return;

            var change = PluginFileChange.Changed(path);
            Schedule(change, change.NewPath!);
        }

        internal void NotifyRenamed(string oldPath, string newPath)
        {
            var oldPluginPath = PluginSourceDiscovery.IsPluginFile(oldPath)
                ? oldPath
                : null;
            var newPluginPath = PluginSourceDiscovery.IsPluginFile(newPath)
                ? newPath
                : null;
            if (oldPluginPath is null && newPluginPath is null)
                return;

            var change = PluginFileChange.Renamed(oldPluginPath, newPluginPath);
            Schedule(change, $"{change.OldPath}|{change.NewPath}");
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
            => NotifyChanged(e.FullPath);

        private void OnFileRenamed(object sender, RenamedEventArgs e)
            => NotifyRenamed(e.OldFullPath, e.FullPath);

        private void Schedule(PluginFileChange change, string key)
        {
            CancellationTokenSource pending;
            lock (_sync)
            {
                if (_disposed)
                    return;

                if (_pending.Remove(key, out var previous))
                {
                    previous.Cancel();
                    previous.Dispose();
                }

                pending = new CancellationTokenSource();
                _pending[key] = pending;
            }

            _ = DispatchAsync(change, key, pending, pending.Token);
        }

        private async Task DispatchAsync(
            PluginFileChange change,
            string key,
            CancellationTokenSource pending,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_debounceDelay, cancellationToken);
                await _onChange(change);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A newer event for the same change key superseded this one.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件文件变更处理失败: {OldPath} -> {NewPath}",
                    change.OldPath, change.NewPath);
            }
            finally
            {
                lock (_sync)
                {
                    if (_pending.TryGetValue(key, out var current)
                        && ReferenceEquals(current, pending))
                    {
                        _pending.Remove(key);
                        pending.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;

                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();

                foreach (var pending in _pending.Values)
                {
                    pending.Cancel();
                    pending.Dispose();
                }
                _pending.Clear();
            }
        }
    }
}
