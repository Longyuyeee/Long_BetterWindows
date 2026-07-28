using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class WorkspaceEscapeRouterTests
{
    [Fact]
    public void Route_ConsumesOnlyHighestPriorityLayer()
    {
        var action = WorkspaceEscapeRouter.Route(new WorkspaceEscapeContext(
            HasTransientLayer: true,
            HasScopedSearchQuery: true,
            CanNavigateBackInModule: true,
            CanNavigateBackInWorkspace: true,
            CanCloseActiveModule: true));

        Assert.Equal(WorkspaceEscapeAction.DismissTransientLayer, action);
    }

    [Theory]
    [InlineData(false, true, true, true, true, (int)WorkspaceEscapeAction.ClearScopedSearch)]
    [InlineData(false, false, true, true, true, (int)WorkspaceEscapeAction.NavigateBackInModule)]
    [InlineData(false, false, false, true, true, (int)WorkspaceEscapeAction.NavigateBackInWorkspace)]
    [InlineData(false, false, false, false, true, (int)WorkspaceEscapeAction.CloseActiveModule)]
    [InlineData(false, false, false, false, false, (int)WorkspaceEscapeAction.None)]
    public void Route_FollowsWorkspaceEscapePriority(
        bool transient,
        bool query,
        bool moduleBack,
        bool workspaceBack,
        bool closeModule,
        int expected)
    {
        var action = WorkspaceEscapeRouter.Route(new WorkspaceEscapeContext(
            transient,
            query,
            moduleBack,
            workspaceBack,
            closeModule));

        Assert.Equal((WorkspaceEscapeAction)expected, action);
    }
}
