using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record PluginRuntimeDiagnosticRow(
        string PluginId,
        string DisplayName,
        string Identity,
        string RegistryState,
        string HealthState,
        string Summary,
        string AccessibilityName,
        PluginRuntimeHealthState HealthStateValue);

    public static class PluginRuntimeDiagnosticPresentation
    {
        public static IReadOnlyList<PluginRuntimeDiagnosticRow> Build(
            IReadOnlyList<PluginRuntimeDiagnosticItem> diagnostics,
            Func<string, string> localize)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            ArgumentNullException.ThrowIfNull(localize);
            return diagnostics
                .OrderBy(item => HealthRank(item.Health.State))
                .ThenBy(item => item.Name ?? item.PluginId, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.PluginId, StringComparer.OrdinalIgnoreCase)
                .Select(item => CreateRow(item, localize))
                .ToArray();
        }

        private static PluginRuntimeDiagnosticRow CreateRow(
            PluginRuntimeDiagnosticItem item,
            Func<string, string> localize)
        {
            var displayName = item.Name ?? item.PluginId;
            var identity = item.Version is null
                ? localize("diagnostics.health.metadataUnavailable")
                : $"{item.Runtime ?? "native"} · v{item.Version}";
            var registryState = localize(
                "diagnostics.health.registry." + item.RegistryState);
            var healthState = localize(
                "diagnostics.health.state." + item.Health.State.ToString().ToLowerInvariant());
            var summary = string.Format(
                localize("diagnostics.health.summary"),
                item.Health.ExecutionCount,
                item.Health.FailureCount,
                item.Health.LifecycleFailureCount);
            return new PluginRuntimeDiagnosticRow(
                item.PluginId,
                displayName,
                identity,
                registryState,
                healthState,
                summary,
                string.Format(
                    localize("diagnostics.health.itemA11y"),
                    displayName,
                    healthState,
                    registryState,
                    summary),
                item.Health.State);
        }

        private static int HealthRank(PluginRuntimeHealthState state) => state switch
        {
            PluginRuntimeHealthState.Unhealthy => 0,
            PluginRuntimeHealthState.Degraded => 1,
            PluginRuntimeHealthState.Healthy => 2,
            _ => 3,
        };
    }
}
