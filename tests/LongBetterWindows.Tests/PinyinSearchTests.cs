using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using QuickLaunchPlugin;
using System.Diagnostics;

namespace LongBetterWindows.Tests;

public class PinyinSearchTests
{
    private const string ScreenColorPicker = "\u5c4f\u5e55\u53d6\u8272";

    [Fact]
    public async Task PinyinService_TransliteratesCharactersBeyondLegacyMap()
    {
        var service = new PinyinService();

        var pinyin = await service.GetPinyinAsync(ScreenColorPicker);
        var initials = await service.GetPinyinInitialsAsync(ScreenColorPicker);

        Assert.True(pinyin.IsSuccess);
        Assert.Equal("pingmuquse", pinyin.Data);
        Assert.True(initials.IsSuccess);
        Assert.Equal("pmqs", initials.Data);
    }

    [Theory]
    [InlineData("pingmuquse")]
    [InlineData("pmqs")]
    [InlineData("pingmuquze")]
    public void CommandRegistry_AutomaticallyMatchesChineseCommand(string query)
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(new PluginManifest
        {
            Id = "color-tools",
            Name = "Color Tools",
            Commands =
            [
                new PluginCommand
                {
                    Id = "screen-color-picker",
                    Title = ScreenColorPicker,
                },
            ],
        });

        var result = Assert.Single(registry.Search(query));

        Assert.Equal("screen-color-picker", result.Descriptor.Command.Id);
    }

    [Fact]
    public void SearchTextMatcher_DoesNotFuzzyMatchShortQueries()
    {
        var result = SearchTextMatcher.Match(
            "ab",
            SearchTextMatcher.CreateForms("ac"));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task CommandRegistry_UserAliasPinyinOutranksWeakTitleMatch()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(new PluginManifest
        {
            Id = "color-tools",
            Name = "Color Tools",
            Commands =
            [
                new PluginCommand
                {
                    Id = "picker",
                    Title = "pingmuquze",
                },
            ],
        });
        var preferences = new CommandPreferenceService(new MemoryStorage());
        await preferences.SetAliasesAsync(
            "color-tools:picker",
            [ScreenColorPicker]);
        registry.AttachPreferences(preferences);

        var result = Assert.Single(registry.Search("pingmuquse"));

        Assert.Equal(760, result.Score);
    }

    [Fact]
    public async Task WorkspaceProvider_MatchesLocalizedDestinationByPinyin()
    {
        var provider = new WorkspaceLauncherSearchProvider(
            new PluginRegistry(),
            key => key == "page.settings.title" ? "\u8bbe\u7f6e" : key);

        var results = await provider.SearchAsync(new SearchRequest(
            "shezhi",
            ContextSnapshot.Empty,
            20));

        var result = Assert.Single(results);
        Assert.Equal("workspace:settings:root", result.Id);
    }

    [Fact]
    public async Task PinyinService_FilterOrdersDirectMatchBeforePinyinMatch()
    {
        var service = new PinyinService();

        var response = await service.FilterAsync(
            [ScreenColorPicker, "pingmuquse helper"],
            "pingmuquse");

        Assert.True(response.IsSuccess);
        Assert.Equal("pingmuquse helper", response.Data![0].Text);
        Assert.Equal(ScreenColorPicker, response.Data[1].Text);
        Assert.True(response.Data[0].Score > response.Data[1].Score);
    }

    [Fact]
    public void CommandRegistry_PinyinSearchStaysWithinInteractiveP95Budget()
    {
        var registry = new CommandRegistry();
        for (var pluginIndex = 0; pluginIndex < 20; pluginIndex++)
        {
            registry.RegisterManifest(new PluginManifest
            {
                Id = $"pinyin.plugin.{pluginIndex}",
                Name = $"\u5de5\u5177\u96c6 {pluginIndex}",
                Commands = Enumerable.Range(0, 50)
                    .Select(commandIndex => new PluginCommand
                    {
                        Id = $"command.{commandIndex}",
                        Title = $"{ScreenColorPicker} {pluginIndex} {commandIndex}",
                    })
                    .ToList(),
            });
        }

        _ = registry.Search("pingmuquse", maxResults: 20);
        var samples = new double[30];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = registry.Search("pingmuquse", maxResults: 20);
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];
        Assert.True(p95 < 100, $"Pinyin command search P95 took {p95:F2}ms.");
    }

    [Theory]
    [InlineData("weixin", 760)]
    [InlineData("wx", 740)]
    [InlineData("weixni", 480)]
    public async Task QuickLaunchMatcher_UsesHostPinyinScores(
        string query,
        int expectedScore)
    {
        var matcher = new QuickLaunchApplicationMatcher(new PinyinService());

        var scores = await matcher.ScoreAsync(
            ["\u5fae\u4fe1", "Notepad"],
            query);

        Assert.Equal(expectedScore, scores["\u5fae\u4fe1"]);
        Assert.DoesNotContain("Notepad", scores.Keys);
    }

    [Fact]
    public async Task QuickLaunchMatcher_FallsBackToDirectSearchWithoutCapability()
    {
        var matcher = new QuickLaunchApplicationMatcher(null);

        var scores = await matcher.ScoreAsync(["Notepad", "Calculator"], "note");

        Assert.Equal(820, scores["Notepad"]);
        Assert.DoesNotContain("Calculator", scores.Keys);
    }

    [Fact]
    public async Task QuickLaunchMatcher_PropagatesCancellation()
    {
        var matcher = new QuickLaunchApplicationMatcher(new PinyinService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            matcher.ScoreAsync(["\u5fae\u4fe1"], "weixin", cancellation.Token));
    }

    [Fact]
    public async Task QuickLaunchMatcher_ThousandApplicationHotQueryMeetsP95Budget()
    {
        var matcher = new QuickLaunchApplicationMatcher(new PinyinService());
        var names = Enumerable.Range(0, 1000)
            .Select(index => $"\u5e94\u7528\u7a0b\u5e8f {index}")
            .ToArray();
        _ = await matcher.ScoreAsync(names, "yingyongchengxu");

        var samples = new double[20];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            _ = await matcher.ScoreAsync(names, "yingyongchengxu");
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];
        Assert.True(p95 < 100, $"QuickLaunch pinyin search P95 took {p95:F2}ms.");
    }

    private sealed class MemoryStorage : IStorageService
    {
        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(null));

        public Task<HostApiResponse> SetAsync(string key, string value)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> DeleteAsync(string key)
            => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(false));
    }
}
