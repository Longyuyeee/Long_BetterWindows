using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class SuperPanelActionCoordinatorTests
{
    [Fact]
    public async Task ContinueSearch_ReturnsContinuationWithoutStartingCommand()
    {
        var coordinator = CreateCoordinator(new PluginRegistry(), out _);
        var callbackCalled = false;
        var selected = Result(new SearchResultAction(
            SearchActionKind.ContinueSearch, "settings "));

        var outcome = await coordinator.ExecuteAsync(
            selected,
            selected.PrimaryAction,
            ContextSnapshot.Empty,
            () =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        Assert.True(outcome.IsSuccess);
        Assert.Equal("settings ", outcome.ContinuationQuery);
        Assert.False(callbackCalled);
    }

    [Fact]
    public async Task MissingCommand_FailsBeforePanelHideCallback()
    {
        var coordinator = CreateCoordinator(new PluginRegistry(), out _);
        var callbackCalled = false;
        var selected = Result(new SearchResultAction(
            SearchActionKind.ExecuteCommand, "missing:open"));

        var outcome = await coordinator.ExecuteAsync(
            selected,
            selected.PrimaryAction,
            ContextSnapshot.Empty,
            () =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.KeepPanelOpen);
        Assert.Equal("操作已失效", outcome.Message);
        Assert.False(callbackCalled);
    }

    [Fact]
    public async Task SuccessfulCommand_HidesBeforeExecutionAndRecordsRecentUse()
    {
        var registry = new PluginRegistry();
        var plugin = new CommandPlugin();
        registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "command.dll",
            Commands =
            [
                new PluginCommand { Id = "open", Title = "Open" },
            ],
        }, plugin, null, "/command");
        var coordinator = CreateCoordinator(registry, out var preferences);
        var callbackCalled = false;
        var selected = Result(new SearchResultAction(
            SearchActionKind.ExecuteCommand, "command:open"));

        var outcome = await coordinator.ExecuteAsync(
            selected,
            selected.PrimaryAction,
            ContextSnapshot.Empty,
            () =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        Assert.True(outcome.IsSuccess, outcome.Message);
        Assert.True(callbackCalled);
        Assert.True(plugin.Executed);
        Assert.Contains(selected.Id, preferences.GetRecentResultIds());
    }

    private static SuperPanelActionCoordinator CreateCoordinator(
        PluginRegistry registry,
        out SearchPreferenceService preferences)
    {
        preferences = new SearchPreferenceService(new MemoryStorage());
        return new SuperPanelActionCoordinator(registry, preferences);
    }

    private static SearchResultItem Result(SearchResultAction action)
        => new()
        {
            Id = "result.test",
            ProviderId = "test",
            Title = "Test",
            PrimaryAction = action,
        };

    private sealed class CommandPlugin : ILongPlugin, IPluginCommandHandler
    {
        public string Id => "command";
        public string Name => "Command";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public bool Executed { get; private set; }

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

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Executed = true;
            return Task.FromResult(PluginCommandResult.Success());
        }
    }

    private sealed class MemoryStorage : IStorageService
    {
        private readonly Dictionary<string, string> _values = new();

        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(
                _values.TryGetValue(key, out var value) ? value : null));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            _values.Remove(key);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(_values.ContainsKey(key)));
    }
}
