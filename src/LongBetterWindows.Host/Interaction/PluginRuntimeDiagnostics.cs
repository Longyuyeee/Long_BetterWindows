using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record PluginRuntimeDiagnosticItem(
        string PluginId,
        string? Name,
        string? Version,
        string? Runtime,
        string RegistryState,
        PluginRuntimeHealthSnapshot Health);

    public static class PluginRuntimeDiagnostics
    {
        public static IReadOnlyList<PluginRuntimeDiagnosticItem> Build(
            PluginRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            var entries = registry.GetAll().ToDictionary(
                entry => entry.Id,
                StringComparer.OrdinalIgnoreCase);
            var pluginIds = entries.Keys
                .Concat(registry.RuntimeHealth.GetAllSnapshots().Select(item => item.PluginId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

            return pluginIds.Select(pluginId =>
            {
                entries.TryGetValue(pluginId, out var entry);
                return new PluginRuntimeDiagnosticItem(
                    pluginId,
                    entry?.DisplayName,
                    entry?.Manifest.Version,
                    entry?.Manifest.Runtime,
                    entry?.State.ToString().ToLowerInvariant() ?? "unloaded",
                    registry.RuntimeHealth.GetSnapshot(pluginId));
            }).ToArray();
        }
    }
}
