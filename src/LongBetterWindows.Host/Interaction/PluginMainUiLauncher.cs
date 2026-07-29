using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal enum PluginMainUiOpenStatus
    {
        Opened,
        PluginMissing,
        StartFailed,
        MainUiUnavailable,
    }

    internal static class PluginMainUiLauncher
    {
        public static async Task<PluginMainUiOpenStatus> OpenAsync(
            PluginRegistry registry,
            string pluginId)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

            var entry = registry.Get(pluginId);
            if (entry is null)
                return PluginMainUiOpenStatus.PluginMissing;

            if (entry.State is not (PluginState.Running or PluginState.Background)
                && !await registry.StartPluginAsync(
                    pluginId,
                    persistAutoStart: false))
            {
                return PluginMainUiOpenStatus.StartFailed;
            }

            if (entry.Instance is not IHasMainUI mainUi)
                return PluginMainUiOpenStatus.MainUiUnavailable;

            mainUi.ShowMainUI();
            return PluginMainUiOpenStatus.Opened;
        }
    }
}
