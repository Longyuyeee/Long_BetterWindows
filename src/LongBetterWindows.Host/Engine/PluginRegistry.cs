using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginRegistry
    {
        private readonly Dictionary<string, PluginEntry> _entries = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
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

        public bool Register(PluginManifest manifest, ILongPlugin instance, PluginLoadContext context, string directory)
        {
            lock (_lock)
            {
                if (_entries.ContainsKey(manifest.Id))
                {
                    Log.Warning("插件 {PluginId} 已注册，跳过重复注册", manifest.Id);
                    return false;
                }

                var entry = new PluginEntry(manifest, instance, directory)
                {
                    State = PluginState.Loaded
                };

                _entries[manifest.Id] = entry;
                Log.Information("插件 {PluginId} (v{Version}) 已注册", manifest.Id, manifest.Version);
                return true;
            }
        }

        public bool Unregister(string pluginId)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(pluginId, out var entry))
                    return false;

                entry.State = PluginState.Disabled;
                _entries.Remove(pluginId);
                Log.Information("插件 {PluginId} 已注销", pluginId);
                return true;
            }
        }

        public bool SetState(string pluginId, PluginState state)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(pluginId, out var entry))
                    return false;

                entry.State = state;
                return true;
            }
        }

        public bool HasCapability(string pluginId, string capability)
        {
            var entry = Get(pluginId);
            return entry != null && entry.HasCapability(capability);
        }

        public IReadOnlyList<string> GetPluginCapabilities(string pluginId)
        {
            var entry = Get(pluginId);
            return entry?.Manifest.Capabilities.AsReadOnly()
                ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        public async Task<bool> StartPluginAsync(string pluginId)
        {
            PluginEntry? entry;
            lock (_lock)
            {
                entry = Get(pluginId);
            }

            if (entry == null || entry.State == PluginState.Running) return false;

            try
            {
                using (PluginAccessContext.Enter(pluginId))
                {
                    var ok = await entry.Instance.StartAsync();
                    if (ok)
                    {
                        SetState(pluginId, PluginState.Running);
                        entry.SetSetting("auto_start", "true");
                        Log.Information("插件 {PluginId} 已启用", pluginId);
                    }
                    return ok;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件 {PluginId} 启动失败", pluginId);
                SetState(pluginId, PluginState.Error);
                return false;
            }
        }

        public async Task<bool> StopPluginAsync(string pluginId)
        {
            PluginEntry? entry;
            lock (_lock)
            {
                entry = Get(pluginId);
            }

            if (entry == null || entry.State != PluginState.Running) return false;

            try
            {
                using (PluginAccessContext.Enter(pluginId))
                {
                    await entry.Instance.StopAsync();
                }
                SetState(pluginId, PluginState.Disabled);
                entry.SetSetting("auto_start", "false");
                Log.Information("插件 {PluginId} 已禁用", pluginId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件 {PluginId} 停止失败", pluginId);
                return false;
            }
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
    }
}
