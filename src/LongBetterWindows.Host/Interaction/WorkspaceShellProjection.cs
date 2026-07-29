namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WorkspaceModuleTabState(
        WorkspaceModuleKey Key,
        string Title,
        bool IsActive,
        bool CanClose,
        string AutomationId,
        string CloseAutomationId,
        string AutomationStatus);

    internal static class WorkspaceShellProjection
    {
        public static IReadOnlyList<WorkspaceModuleTabState> Build(
            WorkspaceNavigationState state,
            Func<WorkspaceModuleDescriptor, string>? titleSelector = null)
        {
            ArgumentNullException.ThrowIfNull(state);
            return state.Modules
                .Select(module =>
                {
                    var isActive = module.Key == state.ActiveModuleKey;
                    return new WorkspaceModuleTabState(
                        module.Key,
                        titleSelector?.Invoke(module) ?? module.Title,
                        isActive,
                        module.CanClose,
                        $"Long.Workspace.ModuleTab.{module.Key}",
                        $"Long.Workspace.ModuleClose.{module.Key}",
                        $"active:{isActive.ToString().ToLowerInvariant()};"
                            + $"closable:{module.CanClose.ToString().ToLowerInvariant()};"
                            + $"key:{module.Key}");
                })
                .ToArray();
        }
    }
}
