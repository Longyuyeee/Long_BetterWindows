using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class LauncherResultProjectionTests
{
    [Fact]
    public void EmptyQuery_ProjectsStableSectionPriorityWithoutDuplicatingResults()
    {
        var results = new[]
        {
            Result("recent", SearchResultKind.Command),
            Result(
                "market",
                SearchResultKind.Data,
                SearchActionKind.OpenWorkspaceModule,
                "marketplace:catalog"),
            Result("context", SearchResultKind.Command),
            Result(
                "management",
                SearchResultKind.Data,
                SearchActionKind.OpenWorkspaceModule,
                "management:root"),
            Result("pinned", SearchResultKind.Command) with { IsPinned = true },
        };
        var context = new ContextSnapshot(
            DateTimeOffset.UtcNow,
            [
                new ContextItem
                {
                    Id = "clipboard",
                    Source = ContextSource.Clipboard,
                    Label = "Clipboard",
                    Text = "text",
                    CompatibleInputTypes = [AcceptedInputType.Text],
                },
            ]);

        var projected = LauncherResultProjection.Build(
            results,
            string.Empty,
            context,
            ["recent"]);

        Assert.Equal(
            ["management", "pinned", "recent", "context", "market"],
            projected.Select(item => item.Id));
        Assert.Equal(
            ["management", "pinned", "recent", "context", "marketplace"],
            projected.Select(item => item.SectionKey));
        Assert.Equal(results.Length, projected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void NonEmptyQuery_UsesSingleSearchResultsSection()
    {
        var projected = LauncherResultProjection.Build(
            [Result("one", SearchResultKind.Command), Result("two", SearchResultKind.Data)],
            "query",
            ContextSnapshot.Empty,
            []);

        Assert.All(projected, item => Assert.Equal("results", item.SectionKey));
    }

    private static SearchResultItem Result(
        string id,
        SearchResultKind kind,
        SearchActionKind actionKind = SearchActionKind.ExecuteCommand,
        string target = "plugin:command")
        => new()
        {
            Id = id,
            ProviderId = "fixture",
            Title = id,
            Kind = kind,
            PrimaryAction = new SearchResultAction(actionKind, target),
        };
}
