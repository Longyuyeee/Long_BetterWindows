using System.IO;
using System.Diagnostics;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

[Collection(PerformanceSensitiveCollection.Name)]
public class SearchCoordinatorTests
{
    [Fact]
    public async Task SearchCoordinator_PublishesIncrementalBatchesAndFinalRanking()
    {
        var coordinator = new SearchCoordinator(new ISearchProvider[]
        {
            new FakeSearchProvider("fast", 10, 10, Result("fast", 200)),
            new FakeSearchProvider("slow", 20, 70, Result("slow", 400)),
        });
        var batches = new List<IReadOnlyList<SearchResultItem>>();

        var final = await coordinator.SearchIncrementalAsync(
            Request(),
            results =>
            {
                batches.Add(results.ToList());
                return Task.CompletedTask;
            });

        Assert.True(batches.Count >= 2);
        Assert.Single(batches[0]);
        Assert.Equal(2, final.Count);
        Assert.Equal("slow", final[0].Id);
    }

    [Fact]
    public async Task SearchCoordinator_TimesOutOneProviderWithoutHidingFastResults()
    {
        var coordinator = new SearchCoordinator(
            new ISearchProvider[]
            {
                new FakeSearchProvider("fast", 10, 0, Result("fast", 200)),
                new NeverSearchProvider(),
            },
            TimeSpan.FromMilliseconds(60));

        var final = await coordinator.SearchIncrementalAsync(Request());

        Assert.Single(final);
        Assert.Equal("fast", final[0].Id);
    }

    [Fact]
    public async Task SearchCoordinator_PublishesFirstResultBelow100msWhileSlowProviderContinues()
    {
        var coordinator = new SearchCoordinator(new ISearchProvider[]
        {
            new FakeSearchProvider("fast", 10, 1, Result("fast", 200)),
            new FakeSearchProvider("slow", 20, 160, Result("slow", 400)),
        });
        _ = await coordinator.SearchIncrementalAsync(Request());
        TimeSpan? firstUpdate = null;
        SearchRunMetrics? metrics = null;
        var started = Stopwatch.GetTimestamp();

        var final = await coordinator.SearchIncrementalAsync(
            Request(),
            results =>
            {
                if (results.Count > 0 && firstUpdate is null)
                    firstUpdate = Stopwatch.GetElapsedTime(started);
                return Task.CompletedTask;
            },
            metricsCompleted: value => metrics = value);

        Assert.NotNull(firstUpdate);
        Assert.True(firstUpdate < TimeSpan.FromMilliseconds(100),
            $"First incremental result took {firstUpdate.Value.TotalMilliseconds:F1}ms.");
        Assert.NotNull(metrics);
        Assert.True(metrics.FirstBatchElapsed < TimeSpan.FromMilliseconds(100));
        Assert.True(metrics.TotalElapsed >= TimeSpan.FromMilliseconds(140));
        Assert.Equal(2, metrics.BatchCount);
        Assert.Equal(2, final.Count);
    }

    [Fact]
    public async Task UnifiedStaticSearch_OneThousandCommandsKeepsP95FirstBatchBelow100ms()
    {
        var registry = new CommandRegistry();
        for (var pluginIndex = 0; pluginIndex < 20; pluginIndex++)
        {
            registry.RegisterManifest(new PluginManifest
            {
                Id = $"performance.plugin.{pluginIndex}",
                Name = $"Performance {pluginIndex}",
                Version = "1.0.0",
                EntryPoint = "performance.dll",
                Commands = Enumerable.Range(0, 50)
                    .Select(commandIndex => new PluginCommand
                    {
                        Id = $"command-{commandIndex}",
                        Title = $"Performance command {pluginIndex:D2}-{commandIndex:D2}",
                    })
                    .ToList(),
            });
        }

        var coordinator = new SearchCoordinator(new ISearchProvider[]
        {
            new StaticCommandSearchProvider(registry),
        });
        await coordinator.SearchIncrementalAsync(Request("performance"));
        var samples = new List<double>();

        for (var iteration = 0; iteration < 20; iteration++)
        {
            SearchRunMetrics? metrics = null;
            await coordinator.SearchIncrementalAsync(
                Request("performance"),
                metricsCompleted: value => metrics = value);
            samples.Add(metrics!.FirstBatchElapsed!.Value.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        Assert.True(p95 < 100,
            $"Unified search P95 first batch was {p95:F1}ms for 1,000 commands.");
    }

    [Fact]
    public async Task SearchCoordinator_DeduplicatesByStableIdAndKeepsHigherScore()
    {
        var coordinator = new SearchCoordinator(new ISearchProvider[]
        {
            new FakeSearchProvider("one", 10, 5, Result("shared", 100)),
            new FakeSearchProvider("two", 20, 10, Result("shared", 300)),
        });

        var result = Assert.Single(await coordinator.SearchIncrementalAsync(Request()));

        Assert.Equal(300, result.Score);
        Assert.Equal("two", result.ProviderId);
    }

    [Fact]
    public async Task StaticCommandProvider_MapsCommandToUnifiedExecutableResult()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(new PluginManifest
        {
            Id = "demo",
            Name = "Demo",
            Version = "1.0.0",
            EntryPoint = "demo.dll",
            Commands = new List<PluginCommand>
            {
                new() { Id = "open", Title = "Open Demo" },
            },
        });
        var provider = new StaticCommandSearchProvider(registry);

        var result = Assert.Single(await provider.SearchAsync(Request("open")));

        Assert.Equal(SearchResultKind.Command, result.Kind);
        Assert.Equal(SearchActionKind.ExecuteCommand, result.PrimaryAction.Kind);
        Assert.Equal("demo:open", result.PrimaryAction.Target);
    }

    [Fact]
    public async Task PluginSearchLifecycle_IsolatesRequestAndRejectsForeignCommands()
    {
        var coordinator = new SearchCoordinator(Array.Empty<ISearchProvider>());
        var registry = new PluginRegistry();
        registry.AttachSearchCoordinator(coordinator);
        var plugin = new FakePluginSearchProvider();
        registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "fake.dll",
            Commands = new List<PluginCommand>
            {
                new() { Id = "open", Title = "Open", AcceptedInputs = new() { AcceptedInputType.Text } },
            },
            Lifecycle = new PluginLifecyclePreference
            {
                CloseBehavior = PluginCloseBehavior.Background,
                SearchInBackground = true,
            },
        }, plugin, null, Path.GetTempPath());
        registry.SetState(plugin.Id, PluginState.Running);
        var sensitiveContext = new ContextSnapshot(DateTimeOffset.UtcNow, new[]
        {
            new ContextItem
            {
                Id = "secret",
                Source = ContextSource.Clipboard,
                Label = "secret label",
                Text = "secret text",
                Paths = new[] { "C:\\secret.txt" },
                ImagePng = new byte[] { 1, 2, 3 },
                CompatibleInputTypes = new[] { AcceptedInputType.Text },
            },
        });

        var result = Assert.Single(await coordinator.SearchIncrementalAsync(
            new SearchRequest(
                "demo",
                sensitiveContext,
                PinnedResultIds:
                [
                    plugin.Id + ":owned",
                    "com.other:foreign",
                ],
                RecentResultIds:
                [
                    plugin.Id + ":recent",
                ])));

        Assert.StartsWith(plugin.Id + ":", result.Id);
        Assert.Equal(plugin.Id + ":open", result.PrimaryAction.Target);
        Assert.Equal("demo", plugin.LastRequest!.Query);
        Assert.Equal(["owned"], plugin.LastRequest.PinnedResultIds);
        Assert.Equal(["recent"], plugin.LastRequest.RecentResultIds);

        registry.SetState(plugin.Id, PluginState.Background);
        Assert.Single(await coordinator.SearchIncrementalAsync(Request("demo")));

        registry.SetState(plugin.Id, PluginState.Stopped);
        Assert.Empty(await coordinator.SearchIncrementalAsync(Request("demo")));
    }

    private static SearchRequest Request(string query = "")
        => new(query, ContextSnapshot.Empty, 20);

    private static SearchResultItem Result(string id, int score) => new()
    {
        Id = id,
        ProviderId = "fake",
        Title = id,
        Score = score,
        PrimaryAction = new SearchResultAction(SearchActionKind.ContinueSearch, id),
    };

    private sealed class FakeSearchProvider : ISearchProvider
    {
        private readonly int _delayMs;
        private readonly SearchResultItem _result;

        public FakeSearchProvider(
            string id,
            int priority,
            int delayMs,
            SearchResultItem result)
            => (Id, Priority, _delayMs, _result) = (id, priority, delayMs, result);

        public string Id { get; }
        public int Priority { get; }

        public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken);
            return new[] { _result };
        }
    }

    private sealed class NeverSearchProvider : ISearchProvider
    {
        public string Id => "never";
        public int Priority => 100;

        public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<SearchResultItem>();
        }
    }

    private sealed class FakePluginSearchProvider :
        ILongPlugin,
        IPluginSearchProvider
    {
        public string Id => "com.long.fake-search";
        public string Name => "Fake Search";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public int Priority => 100;
        public PluginSearchRequest? LastRequest { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<PluginSearchResult>> SearchAsync(
            PluginSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<IReadOnlyList<PluginSearchResult>>(new[]
            {
                new PluginSearchResult
                {
                    Id = "owned",
                    Title = "Owned",
                    Score = 300,
                    PrimaryAction = new PluginSearchAction(
                        PluginSearchActionKind.ExecuteCommand,
                        Id + ":open"),
                },
                new PluginSearchResult
                {
                    Id = "foreign",
                    Title = "Foreign",
                    Score = 500,
                    PrimaryAction = new PluginSearchAction(
                        PluginSearchActionKind.ExecuteCommand,
                        "com.other:open"),
                },
                new PluginSearchResult
                {
                    Id = "continuation",
                    Title = "Continuation",
                    Score = 450,
                    Kind = PluginSearchResultKind.Continuation,
                    PrimaryAction = new PluginSearchAction(
                        PluginSearchActionKind.ContinueSearch,
                        new string('x', 513)),
                },
            });
        }
    }
}
