using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandPreferenceTests
{
    [Fact]
    public async Task UserAliases_AreNormalizedPersistedAndIncludedInSearch()
    {
        var storage = new MemoryStorage();
        var preferences = new CommandPreferenceService(storage);
        var registry = Registry(preferences, new CommandHandler());

        var aliases = await preferences.SetAliasesAsync(
            "demo:demo.open",
            ["  My Tool  ", "my tool", "常用入口"]);

        Assert.Equal(["My Tool", "常用入口"], aliases);
        Assert.Equal(
            "demo:demo.open",
            Assert.Single(registry.Commands.Search("my tool")).Descriptor.Key);
        var reloaded = new CommandPreferenceService(storage);
        await reloaded.InitializeAsync();
        Assert.Equal(aliases, reloaded.Get("demo:demo.open").Aliases);
    }

    [Fact]
    public async Task DisabledCommand_IsExcludedFromSearchAndRejectedByExecutor()
    {
        var preferences = new CommandPreferenceService(new MemoryStorage());
        var handler = new CommandHandler();
        var registry = Registry(preferences, handler);
        await preferences.SetEnabledAsync("demo:demo.open", false);

        Assert.Empty(registry.Commands.Search("Open demo"));
        var result = await new CommandExecutor(registry).ExecuteAsync(
            "demo:demo.open");

        Assert.False(result.IsSuccess);
        Assert.Contains("停用", result.Message);
        Assert.Equal(0, handler.ExecutionCount);
    }

    [Fact]
    public async Task DisabledCommand_IsFilteredFromAnySearchProvider()
    {
        var preferences = new CommandPreferenceService(new MemoryStorage());
        var registry = Registry(preferences, new CommandHandler());
        await preferences.SetEnabledAsync("demo:demo.open", false);
        var coordinator = new SearchCoordinator(
            [new CommandResultProvider()],
            commandEnabled: registry.Commands.IsEnabled);

        var results = await coordinator.SearchIncrementalAsync(
            new SearchRequest("demo", ContextSnapshot.Empty));

        Assert.Empty(results);
    }

    [Fact]
    public async Task RestoringDefaults_RemovesPersistedEntry()
    {
        var storage = new MemoryStorage();
        var preferences = new CommandPreferenceService(storage);
        await preferences.SetEnabledAsync("demo:open", false);
        await preferences.SetAliasesAsync("demo:open", ["custom"]);

        await preferences.SetEnabledAsync("demo:open", true);
        await preferences.SetAliasesAsync("demo:open", Array.Empty<string>());

        Assert.Empty(storage.Values);
        Assert.Equal(CommandPreferenceSnapshot.Default, preferences.Get("demo:open"));
    }

    [Fact]
    public void AliasParser_EnforcesCountAndLengthLimits()
    {
        Assert.Throws<ArgumentException>(() =>
            CommandPreferenceService.ParseAliases(new string('x', 33)));
        Assert.Throws<ArgumentException>(() =>
            CommandPreferenceService.ParseAliases(
                string.Join(',', Enumerable.Range(1, 9).Select(index => $"a{index}"))));
    }

    private static PluginRegistry Registry(
        CommandPreferenceService preferences,
        CommandHandler handler)
    {
        var registry = new PluginRegistry();
        registry.Commands.AttachPreferences(preferences);
        registry.Register(
            new PluginManifest
            {
                Id = "demo",
                Name = "Demo",
                Version = "1.0.0",
                EntryPoint = "demo.dll",
                Commands =
                [
                    new PluginCommand
                    {
                        Id = "demo.open",
                        Title = "Open demo",
                        AcceptedInputs = [AcceptedInputType.None],
                    },
                ],
            },
            handler,
            null,
            "/demo");
        return registry;
    }

    private sealed class CommandHandler : IPluginCommandHandler
    {
        public int ExecutionCount { get; private set; }

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(PluginCommandResult.Success());
        }
    }

    private sealed class CommandResultProvider : ISearchProvider
    {
        public string Id => "custom";
        public int Priority => 10;

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SearchResultItem>>(
            [
                new SearchResultItem
                {
                    Id = "custom:demo",
                    ProviderId = Id,
                    Title = "Demo",
                    PrimaryAction = new SearchResultAction(
                        SearchActionKind.ExecuteCommand,
                        "demo:demo.open"),
                },
            ]);
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
