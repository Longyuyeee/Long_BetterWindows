using System.IO;
using System.Windows;
using System.Windows.Input;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public class SuperPanelInteractionTests
{
    [Fact]
    public void DragSession_AllowsOnlyPinnedOrCustomGroupResults()
    {
        var session = new SuperPanelDragSession();

        Assert.False(session.TryBegin(
            SuperPanelGroupIds.Smart, Result("smart"), new Point(), false));
        Assert.False(session.TryBegin(
            SuperPanelGroupIds.Pinned, Result("unpinned"), new Point(), false));
        Assert.False(session.TryBegin(
            SuperPanelGroupIds.Pinned, Result("button", pinned: true), new Point(), true));
        Assert.True(session.TryBegin(
            SuperPanelGroupIds.Pinned, Result("pinned", pinned: true), new Point(4, 5), false));
        Assert.Equal(SuperPanelGroupIds.Pinned, session.SourceGroupId);
    }

    [Fact]
    public void DragSession_UsesSystemThresholdAndSuppressesOnlyTheFollowingClick()
    {
        var session = new SuperPanelDragSession();
        session.TryBegin(
            "folder:work", Result("run"), new Point(10, 10), originInsideButton: false);

        Assert.False(session.TryStartDrag(
            new Point(12, 13), MouseButtonState.Pressed, 4, 4, out _));
        Assert.True(session.TryStartDrag(
            new Point(15, 11), MouseButtonState.Pressed, 4, 4, out var resultId));
        Assert.Equal("run", resultId);
        Assert.True(session.ConsumeClickSuppression());
        Assert.False(session.ConsumeClickSuppression());
        Assert.Equal("folder:work", session.SourceGroupId);

        session.CompleteDrop();
        Assert.Null(session.SourceGroupId);
    }

    [Fact]
    public void DragSession_ExposesDropTargetRules()
    {
        Assert.True(SuperPanelDragSession.CanDropOnResults(SuperPanelGroupIds.Pinned));
        Assert.True(SuperPanelDragSession.CanDropOnResults("folder:work"));
        Assert.False(SuperPanelDragSession.CanDropOnResults(SuperPanelGroupIds.Recent));
        Assert.True(SuperPanelDragSession.CanDropOnGroup("folder:work"));
        Assert.False(SuperPanelDragSession.CanDropOnGroup(SuperPanelGroupIds.Pinned));
    }

    [Fact]
    public void KeyboardRouter_PrioritizesSecondaryRemovePrimaryAndDismissCommands()
    {
        var selected = Result("run", secondary: true);

        Assert.Equal(
            SuperPanelKeyboardCommand.ExecuteSecondary,
            SuperPanelKeyboardRouter.Resolve(
                Key.Enter, ModifierKeys.Shift, selected, "folder:work"));
        Assert.Equal(
            SuperPanelKeyboardCommand.RemoveFromGroup,
            SuperPanelKeyboardRouter.Resolve(
                Key.Delete, ModifierKeys.None, selected, "folder:work"));
        Assert.Equal(
            SuperPanelKeyboardCommand.ExecutePrimary,
            SuperPanelKeyboardRouter.Resolve(
                Key.Enter, ModifierKeys.None, selected, SuperPanelGroupIds.Smart));
        Assert.Equal(
            SuperPanelKeyboardCommand.Dismiss,
            SuperPanelKeyboardRouter.Resolve(
                Key.Escape, ModifierKeys.None, null, SuperPanelGroupIds.Smart));
        Assert.Equal(
            SuperPanelKeyboardCommand.PreviousPage,
            SuperPanelKeyboardRouter.Resolve(
                Key.PageUp, ModifierKeys.None, null, SuperPanelGroupIds.Smart));
        Assert.Equal(
            SuperPanelKeyboardCommand.NextPage,
            SuperPanelKeyboardRouter.Resolve(
                Key.PageDown, ModifierKeys.None, selected, SuperPanelGroupIds.Smart));
        Assert.Equal(
            SuperPanelKeyboardCommand.None,
            SuperPanelKeyboardRouter.Resolve(
                Key.Delete, ModifierKeys.None, selected, SuperPanelGroupIds.Pinned));
    }

    [Fact]
    public void WindowLifecycle_PositionUsesCursorOffsetWhenSpaceIsAvailable()
    {
        var position = SuperPanelWindowLifecycle.CalculatePosition(
            new Point(100, 80), new Rect(0, 0, 1920, 1080), new Size(600, 400));

        Assert.Equal(new Point(116, 96), position);
    }

    [Fact]
    public void WindowLifecycle_PositionFlipsBeforeRightAndBottomEdges()
    {
        var position = SuperPanelWindowLifecycle.CalculatePosition(
            new Point(1800, 1000), new Rect(0, 0, 1920, 1080), new Size(600, 400));

        Assert.Equal(new Point(1184, 584), position);
    }

    [Fact]
    public void WindowLifecycle_PositionClampsOversizedWindowInsideAvailableOrigin()
    {
        var position = SuperPanelWindowLifecycle.CalculatePosition(
            new Point(250, 180), new Rect(100, 100, 300, 200), new Size(500, 300));

        Assert.Equal(new Point(110, 110), position);
    }

    [Fact]
    public void FocusSensitiveExecution_UsesLifecycleReleaseBeforeCoordinator()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Views",
            "SuperPanelWindow.xaml.cs"));
        var release = source.IndexOf(
            "beforeCommandExecution: _windowLifecycle.ReleaseForegroundAsync",
            StringComparison.Ordinal);
        var execute = source.IndexOf(
            "_actionCoordinator.ExecuteAsync",
            StringComparison.Ordinal);

        Assert.True(execute >= 0);
        Assert.True(release > execute);
        Assert.Equal(
            2,
            source.Split(
                "beforeCommandExecution: _windowLifecycle.ReleaseForegroundAsync",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("CommandPaletteWindow.ShowPalette(intent)", source);
        Assert.DoesNotContain("CommandPaletteWindow.ShowPalette(view.ContinuationQuery", source);
    }

    [Fact]
    public void SecondaryActionMenuProjection_UsesStableLabelsAndAutomationIds()
    {
        var first = new SearchResultAction(SearchActionKind.CopyText, "one");
        var second = new SearchResultAction(
            SearchActionKind.OpenPath, "two", Label: "打开位置");
        var result = Result("menu") with { SecondaryActions = new[] { first, second } };

        var items = SearchResultActionMenuProjection.Build(result);

        Assert.Equal(new[] { "执行", "打开位置" }, items.Select(item => item.Header));
        Assert.Equal(
            new[] { "Long.Result.SecondaryAction.0", "Long.Result.SecondaryAction.1" },
            items.Select(item => item.AutomationId));
        Assert.Same(first, items[0].Action);
        Assert.Same(second, items[1].Action);
    }

    [Fact]
    public void ViewProjection_ProjectsLoadingContext()
    {
        var view = SuperPanelViewProjection.ProjectContext(
            new SuperPanelContextUpdate(ContextSnapshot.Empty, IsLoading: true));

        Assert.Null(view.Items);
        Assert.False(view.ShowBadges);
        Assert.Equal("\u6b63\u5728\u8bfb\u53d6\u5f53\u524d\u4e0a\u4e0b\u6587...", view.Summary);
    }

    [Fact]
    public void ViewProjection_ProjectsEmptyContext()
    {
        var view = SuperPanelViewProjection.ProjectContext(
            new SuperPanelContextUpdate(ContextSnapshot.Empty, IsLoading: false));

        Assert.Empty(view.Items!);
        Assert.False(view.ShowBadges);
        Assert.Equal("\u5e38\u7528\u3001\u56fa\u5b9a\u4e0e\u6700\u8fd1\u64cd\u4f5c", view.Summary);
    }

    [Fact]
    public void ViewProjection_ProjectsCapturedContext()
    {
        var snapshot = new ContextSnapshot(DateTimeOffset.UtcNow, new[]
        {
            new ContextItem
            {
                Id = "clipboard",
                Source = ContextSource.Clipboard,
                Label = "Clipboard",
                CompatibleInputTypes = new[] { AcceptedInputType.Text },
            },
        });

        var view = SuperPanelViewProjection.ProjectContext(
            new SuperPanelContextUpdate(snapshot, IsLoading: false));

        Assert.Same(snapshot.Items, view.Items);
        Assert.True(view.ShowBadges);
        Assert.Equal("\u5df2\u8bc6\u522b 1 \u9879\u4e0a\u4e0b\u6587\uff0c\u64cd\u4f5c\u5c06\u81ea\u52a8\u5339\u914d", view.Summary);
    }

    [Fact]
    public void ViewProjection_ProjectsCurrentLanguage()
    {
        var view = SuperPanelViewProjection.ProjectContext(
            new SuperPanelContextUpdate(ContextSnapshot.Empty, IsLoading: true),
            key => key == "superPanel.context.loading"
                ? "Reading context..."
                : key);

        Assert.Equal("Reading context...", view.Summary);
    }

    [Fact]
    public void ViewProjection_ProjectsActionDisposition()
    {
        var continuation = SuperPanelViewProjection.ProjectAction(new(
            SearchActionKind.ContinueSearch, true, false, "ignored", "next"));
        var completed = SuperPanelViewProjection.ProjectAction(new(
            SearchActionKind.CopyText, true, false, "done"));
        var retained = SuperPanelViewProjection.ProjectAction(new(
            SearchActionKind.CopyText, false, false, "failed"));

        Assert.Equal(SuperPanelActionDisposition.ContinueSearch, continuation.Disposition);
        Assert.Equal("next", continuation.ContinuationQuery);
        Assert.Equal(SuperPanelActionDisposition.Hide, completed.Disposition);
        Assert.Equal("done", completed.Status);
        Assert.Equal(SuperPanelActionDisposition.Present, retained.Disposition);
        Assert.Equal("failed", retained.Status);
    }

    private static SearchResultItem Result(
        string id,
        bool pinned = false,
        bool secondary = false) => new()
    {
        Id = id,
        ProviderId = "test",
        Title = id,
        IsPinned = pinned,
        PrimaryAction = new SearchResultAction(SearchActionKind.ContinueSearch, id),
        SecondaryActions = secondary
            ? new[] { new SearchResultAction(SearchActionKind.ContinueSearch, id) }
            : Array.Empty<SearchResultAction>(),
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(
                   directory.FullName,
                   "LongBetterWindows.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
