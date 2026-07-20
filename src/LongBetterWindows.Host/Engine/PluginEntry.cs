using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Host.Engine
{
    public sealed class PluginEntry
    {
        private Dictionary<string, object> _settings;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public string Id => Manifest.Id;
        public PluginManifest Manifest { get; }
        public object Instance { get; }
        public PluginState State { get; set; } = PluginState.Loaded;
        public string Directory { get; }
        public PluginLifecyclePreference Lifecycle { get; }

        public PluginEntry(PluginManifest manifest, object instance, string directory)
        {
            Manifest = manifest;
            Instance = instance;
            Directory = directory;
            Lifecycle = manifest.Lifecycle ?? new PluginLifecyclePreference();
            _settings = LoadSettings(directory, manifest.DefaultSettings);
        }

        public bool HasCapability(string capability)
            => Manifest.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);

        public string? GetSetting(string key)
        {
            if (_settings.TryGetValue(key, out var val))
            {
                if (val is JsonElement je)
                {
                    return je.ValueKind switch
                    {
                        JsonValueKind.String => je.GetString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Number => je.GetRawText(),
                        _ => null,
                    };
                }
                return val?.ToString();
            }
            return null;
        }

        public void SetSetting(string key, string value)
        {
            _settings[key] = value;
            SaveSettings();
        }

        private Dictionary<string, object> LoadSettings(
            string pluginDir, Dictionary<string, object>? defaults)
        {
            var settings = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // 先加载 manifest 默认值
            if (defaults != null)
            {
                foreach (var kv in defaults)
                    settings[kv.Key] = kv.Value;
            }

            // 再加载 config.json 覆盖
            var configPath = System.IO.Path.Combine(pluginDir, "config.json");
            if (System.IO.File.Exists(configPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var saved = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions);
                    if (saved != null)
                    {
                        foreach (var kv in saved)
                            settings[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }

            return settings;
        }

        private void SaveSettings()
        {
            var configPath = System.IO.Path.Combine(Directory, "config.json");
            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                System.IO.File.WriteAllText(configPath, json);
            }
            catch { }
        }
    }
}
