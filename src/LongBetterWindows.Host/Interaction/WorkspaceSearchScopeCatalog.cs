namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WorkspaceSearchScopeDescriptor(
        string ScopeId,
        string PlaceholderResourceKey);

    internal static class WorkspaceSearchScopeCatalog
    {
        public static WorkspaceSearchScopeDescriptor? Resolve(
            WorkspaceModuleKey key)
            => (key.Kind, key.ResourceId) switch
            {
                ("management", "root") => new(
                    "management",
                    "workspace.search.managementHint"),
                ("marketplace", "catalog") => new(
                    "marketplace",
                    "workspace.search.marketHint"),
                ("management-page", "plugins") => new(
                    "installed-plugins",
                    "workspace.search.pluginsHint"),
                _ => null,
            };
    }
}
