using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record PluginSettingsModuleState(
        string Id,
        string Name,
        string Version,
        string RuntimeLabel,
        PluginState State,
        bool IsRunning,
        string StatusText,
        string ToggleText,
        string Hotkey,
        bool HasHotkey,
        bool HasSettings,
        bool CanOpen,
        int CapabilityCount,
        int TotalCalls,
        DateTime? LastCallTime,
        long RegistrationRevision);

    internal static class PluginSettingsModuleProjection
    {
        public static PluginSettingsModuleState Build(
            PluginEntry entry,
            PluginUsageStats? usage = null,
            Func<string, string>? localize = null)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var isRunning = entry.State is PluginState.Running or PluginState.Background;
            var statusKey = isRunning
                ? "plugins.status.runningVersion"
                : "plugins.status.stoppedVersion";
            var statusFormat = Text(
                localize,
                statusKey,
                isRunning ? "Running · v{0}" : "Stopped · v{0}");

            return new PluginSettingsModuleState(
                entry.Id,
                entry.DisplayName,
                entry.Manifest.Version,
                entry.Manifest.Runtime switch
                {
                    "webview" => "Web",
                    "csharp-script" => "Script",
                    _ => "DLL",
                },
                entry.State,
                isRunning,
                string.Format(statusFormat, entry.Manifest.Version),
                Text(
                    localize,
                    isRunning ? "action.disable" : "action.enable",
                    isRunning ? "Disable" : "Enable"),
                PluginRegistry.GetPluginHotkey(entry) ?? string.Empty,
                !string.IsNullOrWhiteSpace(PluginRegistry.GetPluginHotkey(entry)),
                entry.Instance is IHasSettingsUI,
                entry.Instance is IHasMainUI
                    || entry.Manifest.Window is not null
                    || !string.IsNullOrWhiteSpace(entry.Manifest.EntryPoint),
                entry.Manifest.Capabilities.Count,
                usage?.TotalCalls ?? 0,
                usage?.LastCallTime,
                entry.RegistrationRevision);
        }

        private static string Text(
            Func<string, string>? localize,
            string key,
            string fallback)
        {
            var value = localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
