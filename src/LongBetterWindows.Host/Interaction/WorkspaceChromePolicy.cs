namespace LongBetterWindows.Host.Interaction
{
    internal static class WorkspaceChromePolicy
    {
        public static bool ShowsInstalledPluginRail(WorkspaceModuleKey activeModuleKey)
            => (activeModuleKey.Kind, activeModuleKey.ResourceId) switch
            {
                ("marketplace", _) => true,
                ("plugin-settings", _) => true,
                ("management-page", "plugins") => true,
                _ => false,
            };
    }
}
