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

    [Theory]
    [InlineData("快捷启动器", "命令", "快")]
    [InlineData("url toolkit", "命令", "U")]
    [InlineData("🧰 Tools", "命令", "🧰")]
    [InlineData("", "翻译文本", "翻")]
    public void PluginWithoutImage_UsesFirstUnicodeTextElementAsIdentityBadge(
        string source,
        string title,
        string expected)
    {
        var result = Result("plugin", SearchResultKind.Command) with
        {
            Source = source,
            Title = title,
            IconKind = SearchResultIconKind.Plugin,
        };

        var projected = Assert.Single(LauncherResultProjection.Build(
            [result],
            string.Empty,
            ContextSnapshot.Empty,
            []));

        Assert.Equal(expected, result.IconLabel);
        Assert.True(result.HasIconLabel);
        Assert.Equal(expected, projected.IconLabel);
        Assert.True(projected.HasIconLabel);
    }

    [Fact]
    public void ImageOrNonPluginResult_DoesNotExposeIdentityBadge()
    {
        var image = Result("image", SearchResultKind.Command) with
        {
            Source = "URL Toolkit",
            IconKind = SearchResultIconKind.Plugin,
            IconPath = "icon.png",
        };
        var management = Result("management", SearchResultKind.Data) with
        {
            Source = "管理中心",
            IconKind = SearchResultIconKind.Management,
        };

        Assert.False(image.HasIconLabel);
        Assert.False(management.HasIconLabel);
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
