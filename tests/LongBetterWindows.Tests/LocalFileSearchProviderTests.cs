using System.IO;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class LocalFileSearchProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"long-local-search-{Guid.NewGuid():N}");

    public LocalFileSearchProviderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "deep", "workspace"));
        File.WriteAllText(
            Path.Combine(_root, "deep", "workspace", "project-needle.txt"),
            "content");
    }

    [Fact]
    public async Task SearchAsync_FindsNestedFilesAndProvidesSecondaryActions()
    {
        var provider = new LocalFileSearchProvider(new[] { _root });

        var result = await SearchUntilSingleAsync(
            provider,
            new SearchRequest("needle", ContextSnapshot.Empty, MaxResults: 10));
        Assert.Equal(SearchActionKind.OpenPath, result.PrimaryAction.Kind);
        Assert.Equal(
            new[] { SearchActionKind.OpenContainingFolder, SearchActionKind.CopyText },
            result.SecondaryActions.Select(action => action.Kind));
        Assert.True(result.HasSecondaryActions);
        Assert.DoesNotContain(_root, result.Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_ShortQueryDoesNotReturnResults()
    {
        var provider = new LocalFileSearchProvider(new[] { _root });

        var results = await provider.SearchAsync(new SearchRequest(
            "n", ContextSnapshot.Empty, MaxResults: 10));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_UsesCurrentLocalizedProjection()
    {
        var provider = new LocalFileSearchProvider(
            new[] { _root },
            key => key switch
            {
                "search.local.file" => "Local file",
                "search.action.open" => "Open",
                "search.action.openContainingFolder" => "Open containing folder",
                "search.action.copyPath" => "Copy path",
                _ => key,
            });

        var result = await SearchUntilSingleAsync(
            provider,
            new SearchRequest("needle", ContextSnapshot.Empty, MaxResults: 10));

        Assert.Equal("Local file", result.Source);
        Assert.Equal("Open", result.PrimaryAction.Label);
        Assert.Equal(
            new[] { "Open containing folder", "Copy path" },
            result.SecondaryActions.Select(action => action.Label));
    }

    [Fact]
    public async Task SearchAsync_RespectsSmallResultLimitWithoutProjectingWholeIndex()
    {
        for (var index = 0; index < 30; index++)
        {
            File.WriteAllText(
                Path.Combine(_root, $"needle-{index:D2}.txt"),
                "content");
        }
        var provider = new LocalFileSearchProvider(new[] { _root });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IReadOnlyList<SearchResultItem> results = [];
        while (results.Count < 3)
        {
            results = await provider.SearchAsync(
                new SearchRequest(
                    "needle",
                    ContextSnapshot.Empty,
                    MaxResults: 3),
                timeout.Token);
            if (results.Count < 3)
                await Task.Delay(100, timeout.Token);
        }

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void WebPluginTemplate_UsesLongUiKitAndEscapesPluginName()
    {
        var html = PluginDevTools.BuildWebPluginTemplate("<Demo>\" Plugin");

        Assert.Contains("long-page", html);
        Assert.Contains("long-card", html);
        Assert.Contains("long-button--primary", html);
        Assert.Contains("&lt;Demo&gt;&quot; Plugin", html);
        Assert.DoesNotContain("#007AFF", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#1E1F22", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebPluginTemplate_UsesExplicitLanguageAndEscapesLocalizedText()
    {
        var html = PluginDevTools.BuildWebPluginTemplate(
            "Demo",
            "en-US",
            key => key.EndsWith("toastSent", StringComparison.Ordinal)
                ? "Sent 'safely'"
                : "<Localized>");

        Assert.Contains("<html lang=\"en-US\">", html);
        Assert.Contains("&lt;Localized&gt;", html);
        Assert.Contains("\"Sent \\u0027safely\\u0027\"", html);
        Assert.DoesNotContain(">开始创作<", html);
        Assert.DoesNotContain("Toast 已发送", html);
    }

    private static async Task<SearchResultItem> SearchUntilSingleAsync(
        LocalFileSearchProvider provider,
        SearchRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var results = await provider.SearchAsync(request, timeout.Token);
            if (results.Count > 0)
                return Assert.Single(results);
            await Task.Delay(100, timeout.Token);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
