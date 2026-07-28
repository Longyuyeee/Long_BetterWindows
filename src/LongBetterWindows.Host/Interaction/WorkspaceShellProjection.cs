namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WorkspaceModuleTabState(
        WorkspaceModuleKey Key,
        string Title,
        bool IsActive,
        bool CanClose);

    internal static class WorkspaceShellProjection
    {
        public static IReadOnlyList<WorkspaceModuleTabState> Build(
            WorkspaceNavigationState state,
            Func<WorkspaceModuleDescriptor, string>? titleSelector = null)
        {
            ArgumentNullException.ThrowIfNull(state);
            return state.Modules
                .Select(module => new WorkspaceModuleTabState(
                    module.Key,
                    titleSelector?.Invoke(module) ?? module.Title,
                    module.Key == state.ActiveModuleKey,
                    module.CanClose))
                .ToArray();
        }
    }
}
