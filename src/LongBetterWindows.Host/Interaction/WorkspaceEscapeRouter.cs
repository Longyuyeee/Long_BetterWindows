namespace LongBetterWindows.Host.Interaction
{
    internal readonly record struct WorkspaceEscapeContext(
        bool HasTransientLayer,
        bool HasScopedSearchQuery,
        bool CanNavigateBackInModule,
        bool CanNavigateBackInWorkspace,
        bool CanCloseActiveModule,
        bool CanReturnToLauncher = false);

    internal enum WorkspaceEscapeAction
    {
        None,
        DismissTransientLayer,
        ClearScopedSearch,
        NavigateBackInModule,
        NavigateBackInWorkspace,
        CloseActiveModule,
        ReturnToLauncher,
    }

    internal static class WorkspaceEscapeRouter
    {
        public static WorkspaceEscapeAction Route(WorkspaceEscapeContext context)
        {
            if (context.HasTransientLayer)
                return WorkspaceEscapeAction.DismissTransientLayer;
            if (context.HasScopedSearchQuery)
                return WorkspaceEscapeAction.ClearScopedSearch;
            if (context.CanNavigateBackInModule)
                return WorkspaceEscapeAction.NavigateBackInModule;
            if (context.CanNavigateBackInWorkspace)
                return WorkspaceEscapeAction.NavigateBackInWorkspace;
            if (context.CanCloseActiveModule)
                return WorkspaceEscapeAction.CloseActiveModule;
            if (context.CanReturnToLauncher)
                return WorkspaceEscapeAction.ReturnToLauncher;
            return WorkspaceEscapeAction.None;
        }
    }
}
