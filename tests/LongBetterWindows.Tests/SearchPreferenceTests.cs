using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class SearchPreferenceTests
{
    [Fact]
    public async Task PreferenceStore_PersistsOnlyStableResultMetadata()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);

        await preferences.TogglePinnedAsync("command:demo:open");
        await preferences.RecordUseAsync("command:demo:open");

        var raw = Assert.Single(storage.Values).Value;
        Assert.Contains("command:demo:open", raw);
        Assert.DoesNotContain("secret query", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Title", raw, StringComparison.OrdinalIgnoreCase);

        var reloaded = new SearchPreferenceService(storage);
        await reloaded.InitializeAsync();
        Assert.Equal("command:demo:open", Assert.Single(reloaded.GetPinnedResultIds()));
        Assert.Equal("command:demo:open", Assert.Single(reloaded.GetRecentResultIds()));
    }

    [Fact]
    public async Task PinnedResult_OutranksHigherBaseScore()
    {
        var preferences = new SearchPreferenceService(new MemoryStorage());
        await preferences.TogglePinnedAsync("low");
        var coordinator = new SearchCoordinator(
            new[] { new FixedProvider() },
            preferences: preferences);

        var results = await coordinator.SearchIncrementalAsync(
            new SearchRequest("demo", ContextSnapshot.Empty));

        Assert.Equal("low", results[0].Id);
        Assert.True(results[0].IsPinned);
        Assert.True(results[0].PreferenceScore > 1000);
    }

    [Fact]
    public async Task RecentCommand_IsRecalledForEmptyQueryWithoutPersistingInvocation()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);
        var registry = new CommandRegistry();
        registry.RegisterManifest(new PluginManifest
        {
            Id = "demo",
            Name = "Demo",
            Version = "1.0.0",
            EntryPoint = "demo.dll",
            Commands = new List<PluginCommand>
            {
                new()
                {
                    Id = "file-only",
                    Title = "File Only",
                    AcceptedInputs = new() { AcceptedInputType.File },
                },
            },
        });
        await preferences.RecordUseAsync("command:demo:file-only");
        var coordinator = new SearchCoordinator(
            new[] { new StaticCommandSearchProvider(registry) },
            preferences: preferences);

        var result = Assert.Single(await coordinator.SearchIncrementalAsync(
            new SearchRequest(string.Empty, ContextSnapshot.Empty)));

        Assert.Equal("command:demo:file-only", result.Id);
        Assert.True(result.PreferenceScore > 0);
    }

    [Fact]
    public async Task CustomGroupCommand_IsRecalledWithoutBeingPinnedOrRecent()
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
                new()
                {
                    Id = "folder-only",
                    Title = "Folder Only",
                    AcceptedInputs = new() { AcceptedInputType.File },
                },
            },
        });
        var coordinator = new SearchCoordinator(
            new[] { new StaticCommandSearchProvider(registry) },
            preferences: new SearchPreferenceService(new MemoryStorage()));

        var result = Assert.Single(await coordinator.SearchIncrementalAsync(
            new SearchRequest(
                string.Empty,
                ContextSnapshot.Empty,
                AdditionalPreferredResultIds: new[] { "command:demo:folder-only" })));

        Assert.Equal("command:demo:folder-only", result.Id);
    }

    [Fact]
    public async Task Clear_RemovesPinnedAndRecentPreferences()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);
        await preferences.TogglePinnedAsync("one");
        await preferences.RecordUseAsync("two");

        await preferences.ClearAsync();

        Assert.Empty(preferences.GetPinnedResultIds());
        Assert.Empty(preferences.GetRecentResultIds());
        Assert.Empty(storage.Values);
    }

    [Fact]
    public async Task PinnedOrder_ReordersAndPersistsOnlyStableIds()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);
        await preferences.TogglePinnedAsync("command:one:open");
        await preferences.TogglePinnedAsync("command:two:open");
        await preferences.TogglePinnedAsync("command:three:open");

        Assert.True(await preferences.MovePinnedAsync("command:three:open", 0));
        Assert.Equal(new[]
        {
            "command:three:open",
            "command:one:open",
            "command:two:open",
        }, preferences.GetPinnedResultIds());

        var reloaded = new SearchPreferenceService(storage);
        await reloaded.InitializeAsync();
        Assert.Equal(preferences.GetPinnedResultIds(), reloaded.GetPinnedResultIds());
        var raw = Assert.Single(storage.Values).Value;
        Assert.DoesNotContain("Title", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Query", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuperPanelOrganizer_ProjectsGroupsWithoutChangingUnifiedRanking()
    {
        var results = new[]
        {
            Result("a", 300),
            Result("b", 200),
            Result("c", 100),
        };

        var smart = SuperPanelResultOrganizer.SelectGroup(
            results, SuperPanelGroupIds.Smart, new[] { "b", "a" }, new[] { "c" });
        var pinned = SuperPanelResultOrganizer.SelectGroup(
            results, SuperPanelGroupIds.Pinned, new[] { "b", "a" }, new[] { "c" });
        var recent = SuperPanelResultOrganizer.SelectGroup(
            results, SuperPanelGroupIds.Recent, new[] { "b", "a" }, new[] { "c", "b" });

        Assert.Equal(new[] { "a", "b", "c" }, smart.Select(item => item.Id));
        Assert.Equal(new[] { "b", "a" }, pinned.Select(item => item.Id));
        Assert.Equal(new[] { "c", "b" }, recent.Select(item => item.Id));
    }

    [Fact]
    public async Task CustomGroups_PersistStableIdsAndSupportMembershipOrder()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var firstMaybe = await groups.CreateAsync(" 工作 流 ");
        var secondMaybe = await groups.CreateAsync("工作 流");
        Assert.NotNull(firstMaybe);
        Assert.NotNull(secondMaybe);
        var first = firstMaybe!;
        var second = secondMaybe!;
        Assert.Equal("工作 流", first.Title);
        Assert.Equal("工作 流 2", second.Title);

        await groups.AddResultAsync(first.Id, "command:one:open");
        await groups.AddResultAsync(first.Id, "command:two:open");
        await groups.AddResultAsync(first.Id, "command:three:open");
        Assert.True(await groups.MoveResultAsync(first.Id, "command:three:open", 0));

        var reloaded = new SuperPanelGroupService(storage);
        await reloaded.InitializeAsync();
        var persisted = reloaded.GetGroups().Single(group => group.Id == first.Id);
        Assert.Equal(new[]
        {
            "command:three:open",
            "command:one:open",
            "command:two:open",
        }, persisted.ResultIds);
        var raw = storage.Values["super-panel.groups.v1"];
        Assert.DoesNotContain("Query", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Clipboard", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomGroups_RenameRemoveDeleteAndClearAreConsistent()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var groupMaybe = await groups.CreateAsync("常用");
        Assert.NotNull(groupMaybe);
        var group = groupMaybe!;
        await groups.AddResultAsync(group.Id, "one");
        Assert.True(await groups.RenameAsync(group.Id, "效率"));
        Assert.True(await groups.RemoveResultAsync(group.Id, "one"));
        Assert.Empty(Assert.Single(groups.GetGroups()).ResultIds);
        Assert.True(await groups.DeleteAsync(group.Id));
        Assert.Empty(groups.GetGroups());

        await groups.CreateAsync("稍后清理");
        await groups.ClearAsync();
        Assert.Empty(groups.GetGroups());
        Assert.DoesNotContain("super-panel.groups.v1", storage.Values.Keys);
    }

    [Fact]
    public void SuperPanelOrganizer_ProjectsCustomFolderInPersistedOrder()
    {
        var results = new[] { Result("a", 300), Result("b", 200), Result("c", 100) };

        var folder = SuperPanelResultOrganizer.SelectGroup(
            results,
            "folder:quality",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "c", "a" });

        Assert.Equal(new[] { "c", "a" }, folder.Select(item => item.Id));
    }

    private static SearchResultItem Result(string id, int score) => new()
    {
        Id = id,
        ProviderId = "test",
        Title = id,
        Score = score,
        PrimaryAction = new SearchResultAction(SearchActionKind.ContinueSearch, id),
    };

    private sealed class FixedProvider : ISearchProvider
    {
        public string Id => "fixed";
        public int Priority => 0;

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SearchResultItem>>(new[]
            {
                Item("high", 900),
                Item("low", 10),
            });

        private static SearchResultItem Item(string id, int score) => new()
        {
            Id = id,
            ProviderId = "fixed",
            Title = id,
            Score = score,
            CanPin = true,
            PrimaryAction = new SearchResultAction(SearchActionKind.ContinueSearch, id),
        };
    }

    private sealed class MemoryStorage : IStorageService
    {
        public Dictionary<string, string> Values { get; } = new();

        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(
                Values.TryGetValue(key, out var value) ? value : null));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            Values.Remove(key);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(Values.ContainsKey(key)));
    }
}
