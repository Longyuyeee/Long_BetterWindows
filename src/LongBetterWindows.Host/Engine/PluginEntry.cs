using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Host.Engine
{
    internal enum AutoStartSettingSource
    {
        LifecycleDefault,
        ManifestDefault,
        LegacyUnknown,
        User,
    }

    internal sealed record AutoStartPreference(
        bool Enabled,
        AutoStartSettingSource Source);

    public sealed class PluginEntry
    {
        private Dictionary<string, object> _settings;
        private object? _instance;
        private readonly Func<PluginEntry, Task<object?>>? _activator;
        private readonly SemaphoreSlim _activationGate = new(1, 1);
        internal SemaphoreSlim LifecycleGate { get; } = new(1, 1);
        private readonly HashSet<string> _persistedSettings =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public string Id => Manifest.Id;
        public string DisplayName => GetLocalizedString(
            "plugin.name",
            Manifest.Name);
        public PluginManifest Manifest { get; }
        public object? Instance => Volatile.Read(ref _instance);
        public bool IsActivated => Instance is not null;
        public PluginState State { get; set; } = PluginState.Loaded;
        public string Directory { get; }
        public PluginLifecyclePreference Lifecycle { get; }
        public long RegistrationRevision { get; }

        public PluginEntry(
            PluginManifest manifest,
            object instance,
            string directory,
            long registrationRevision)
        {
            Manifest = manifest;
            _instance = instance;
            Directory = directory;
            Lifecycle = manifest.Lifecycle ?? new PluginLifecyclePreference();
            RegistrationRevision = registrationRevision;
            _settings = LoadSettings(directory, manifest.DefaultSettings);
        }

        internal PluginEntry(
            PluginManifest manifest,
            string directory,
            long registrationRevision,
            Func<PluginEntry, Task<object?>> activator)
        {
            Manifest = manifest;
            Directory = directory;
            Lifecycle = manifest.Lifecycle ?? new PluginLifecyclePreference();
            RegistrationRevision = registrationRevision;
            _activator = activator ?? throw new ArgumentNullException(nameof(activator));
            _settings = LoadSettings(directory, manifest.DefaultSettings);
        }

        internal async Task<bool> EnsureActivatedAsync()
        {
            if (Instance is not null)
                return true;
            if (_activator is null)
                return false;

            await _activationGate.WaitAsync();
            try
            {
                if (Instance is not null)
                    return true;

                var instance = await _activator(this);
                if (instance is null)
                    return false;
                Volatile.Write(ref _instance, instance);
                return true;
            }
            finally
            {
                _activationGate.Release();
            }
        }

        public bool HasCapability(string capability)
            => Manifest.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);

        public PluginLanguageContext? LanguageContext { get; private set; }

        internal void ApplyLanguageContext(PluginLanguageContext context)
            => LanguageContext = context;

        public string GetLocalizedString(string key, string fallback)
            => LanguageContext?.Resources.TryGetValue(key, out var value) == true
                && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : fallback;

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
            _persistedSettings.Add(key);
            SaveSettings();
        }

        internal AutoStartPreference GetAutoStartPreference()
        {
            var configured = GetSetting("auto_start");
            var enabled = bool.TryParse(configured, out var parsed)
                ? parsed
                : Lifecycle.StartWithHost;
            if (_persistedSettings.Contains("auto_start"))
            {
                var source = GetSetting("auto_start_source");
                return new AutoStartPreference(
                    enabled,
                    string.Equals(source, "user", StringComparison.OrdinalIgnoreCase)
                        ? AutoStartSettingSource.User
                        : AutoStartSettingSource.LegacyUnknown);
            }

            return new AutoStartPreference(
                enabled,
                Manifest.DefaultSettings?.ContainsKey("auto_start") == true
                    ? AutoStartSettingSource.ManifestDefault
                    : AutoStartSettingSource.LifecycleDefault);
        }

        internal void SetAutoStart(bool enabled)
        {
            _settings["auto_start"] = enabled ? "true" : "false";
            _settings["auto_start_source"] = "user";
            _persistedSettings.Add("auto_start");
            _persistedSettings.Add("auto_start_source");
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
                        {
                            settings[kv.Key] = kv.Value;
                            _persistedSettings.Add(kv.Key);
                        }
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
