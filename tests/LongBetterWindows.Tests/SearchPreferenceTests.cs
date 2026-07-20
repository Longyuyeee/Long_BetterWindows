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

    [Fact]
    public async Task SuperPanelCoordinator_ProjectsPinnedRecentAndCustomGroups()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);
        var groups = new SuperPanelGroupService(storage);
        await preferences.TogglePinnedAsync("b");
        await preferences.RecordUseAsync("c");
        var custom = await groups.CreateAsync("Work");
        Assert.NotNull(custom);
        await groups.AddResultAsync(custom!.Id, "a");
        var coordinator = new SuperPanelGroupCoordinator(preferences, groups);
        coordinator.SetResults(
            [Result("a", 300), Result("b", 200), Result("c", 100)],
            completed: true);

        Assert.True(coordinator.SelectGroup(SuperPanelGroupIds.Pinned));
        Assert.Equal(["b"], coordinator.BuildView().VisibleResults.Select(x => x.Id));
        Assert.True(coordinator.SelectGroup(SuperPanelGroupIds.Recent));
        Assert.Equal(["c"], coordinator.BuildView().VisibleResults.Select(x => x.Id));
        Assert.True(coordinator.SelectGroup(custom.Id));
        var customView = coordinator.BuildView();
        Assert.Equal(["a"], customView.VisibleResults.Select(x => x.Id));
        Assert.True(customView.ShowCustomGroupActions);
        Assert.Equal(1, customView.Groups.Single(x => x.Id == custom.Id).Count);
    }

    [Fact]
    public void SuperPanelCoordinator_CyclesBuiltInGroupsAndRejectsUnknownGroup()
    {
        var storage = new MemoryStorage();
        var coordinator = new SuperPanelGroupCoordinator(
            new SearchPreferenceService(storage),
            new SuperPanelGroupService(storage));

        Assert.Equal(SuperPanelGroupIds.Smart, coordinator.ActiveGroupId);
        Assert.True(coordinator.Cycle(-120));
        Assert.Equal(SuperPanelGroupIds.Pinned, coordinator.ActiveGroupId);
        Assert.True(coordinator.Cycle(120));
        Assert.Equal(SuperPanelGroupIds.Smart, coordinator.ActiveGroupId);
        Assert.False(coordinator.SelectGroup("unknown"));
        Assert.Equal(SuperPanelGroupIds.Smart, coordinator.ActiveGroupId);
    }

    [Fact]
    public void SuperPanelCoordinator_ShowsEmptyStateOnlyAfterSearchCompletes()
    {
        var storage = new MemoryStorage();
        var coordinator = new SuperPanelGroupCoordinator(
            new SearchPreferenceService(storage),
            new SuperPanelGroupService(storage));
        coordinator.SelectGroup(SuperPanelGroupIds.Pinned);

        coordinator.SetResults(Array.Empty<SearchResultItem>(), completed: false);
        Assert.False(coordinator.BuildView().ShowEmptyState);

        coordinator.SetResults(Array.Empty<SearchResultItem>(), completed: true);
        var completed = coordinator.BuildView();
        Assert.True(completed.ShowEmptyState);
        Assert.Equal("还没有固定操作", completed.EmptyStateText);
        Assert.Equal("当前分组为空", completed.StatusText);
    }

    [Fact]
    public async Task SuperPanelCoordinator_CreatesRenamesAndDeletesActiveGroup()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var coordinator = new SuperPanelGroupCoordinator(
            new SearchPreferenceService(storage), groups);

        var created = await coordinator.SaveGroupAsync(null, "Work");
        Assert.True(created.Success);
        Assert.Equal("Work", coordinator.ActiveCustomGroup?.Title);

        var renamed = await coordinator.SaveGroupAsync(
            coordinator.ActiveGroupId, "Focused Work");
        Assert.True(renamed.Success);
        Assert.Equal("Focused Work", coordinator.ActiveCustomGroup?.Title);

        var deleted = await coordinator.DeleteActiveGroupAsync();
        Assert.True(deleted.Success);
        Assert.Equal(SuperPanelGroupIds.Pinned, coordinator.ActiveGroupId);
        Assert.Empty(groups.GetGroups());
    }

    [Fact]
    public async Task SuperPanelCoordinator_ReordersPinnedAndCustomResults()
    {
        var storage = new MemoryStorage();
        var preferences = new SearchPreferenceService(storage);
        var groups = new SuperPanelGroupService(storage);
        await preferences.TogglePinnedAsync("a");
        await preferences.TogglePinnedAsync("b");
        var coordinator = new SuperPanelGroupCoordinator(preferences, groups);
        coordinator.SelectGroup(SuperPanelGroupIds.Pinned);

        Assert.True((await coordinator.ReorderActiveResultAsync("b", 0)).Success);
        Assert.Equal(new[] { "b", "a" }, preferences.GetPinnedResultIds());

        var custom = await groups.CreateAsync("Work");
        Assert.NotNull(custom);
        await groups.AddResultAsync(custom!.Id, "a");
        await groups.AddResultAsync(custom.Id, "b");
        coordinator.SelectGroup(custom.Id);
        Assert.True((await coordinator.ReorderActiveResultAsync("b", 0)).Success);
        Assert.Equal(new[] { "b", "a" }, groups.GetGroups().Single().ResultIds);
    }

    [Fact]
    public async Task SuperPanelCoordinator_MovesAndRemovesResultsAcrossCustomGroups()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var source = await groups.CreateAsync("Source");
        var target = await groups.CreateAsync("Target");
        Assert.NotNull(source);
        Assert.NotNull(target);
        await groups.AddResultAsync(source!.Id, "command:test:run");
        var coordinator = new SuperPanelGroupCoordinator(
            new SearchPreferenceService(storage), groups);
        coordinator.SelectGroup(source.Id);

        var moved = await coordinator.MoveResultToGroupAsync(
            source.Id, target!.Id, "command:test:run");
        Assert.True(moved.Success);
        Assert.Equal(target.Id, coordinator.ActiveGroupId);
        Assert.Empty(groups.GetGroups().Single(group => group.Id == source.Id).ResultIds);
        Assert.Equal(
            new[] { "command:test:run" },
            groups.GetGroups().Single(group => group.Id == target.Id).ResultIds);

        Assert.True((await coordinator.RemoveFromActiveGroupAsync("command:test:run")).Success);
        Assert.Empty(groups.GetGroups().Single(group => group.Id == target.Id).ResultIds);
    }

    [Fact]
    public void SuperPanelGroupEditor_OpensRenameStateAndCancelsCleanly()
    {
        var storage = new MemoryStorage();
        var coordinator = new SuperPanelGroupCoordinator(
            new SearchPreferenceService(storage),
            new SuperPanelGroupService(storage));
        var editor = new SuperPanelGroupEditorSession(coordinator);

        editor.Open("folder:work", "Work");
        Assert.True(editor.State.IsOpen);
        Assert.Equal("重命名操作分组", editor.State.Heading);
        Assert.Equal("Work", editor.State.Title);

        editor.Cancel();
        Assert.Equal(SuperPanelGroupEditorState.Closed, editor.State);
    }

    [Fact]
    public async Task SuperPanelGroupEditor_KeepsInvalidSaveOpenAndClosesAfterSuccess()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var editor = new SuperPanelGroupEditorSession(
            new SuperPanelGroupCoordinator(new SearchPreferenceService(storage), groups));
        editor.Open(null, string.Empty);

        var invalid = await editor.SaveAsync("  ");
        Assert.False(invalid.Success);
        Assert.True(editor.State.IsOpen);

        var saved = await editor.SaveAsync("Work");
        Assert.True(saved.Success);
        Assert.Equal(SuperPanelGroupEditorState.Closed, editor.State);
        Assert.Equal("Work", Assert.Single(groups.GetGroups()).Title);
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
