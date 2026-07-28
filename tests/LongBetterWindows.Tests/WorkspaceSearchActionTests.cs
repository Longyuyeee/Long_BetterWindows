using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkspaceSearchActionTests
{
    [Fact]
    public async Task ExecuteAsync_ValidWorkspaceAddress_UsesCanonicalLauncherTarget()
    {
        string? launchedTarget = null;
        var executor = new SearchResultActionExecutor(
            new PluginRegistry(),
            workspaceModuleLauncher: (target, _) =>
            {
                launchedTarget = target;
                return Task.FromResult(PluginCommandResult.Success());
            });

        var result = await executor.ExecuteAsync(
            new SearchResultAction(
                SearchActionKind.OpenWorkspaceModule,
                " MARKETPLACE:CATALOG "),
            ContextSnapshot.Empty);

        Assert.True(result.IsSuccess);
        Assert.Equal("marketplace:catalog", launchedTarget);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidWorkspaceAddress_FailsBeforeLauncher()
    {
        var launcherCalled = false;
        var executor = new SearchResultActionExecutor(
            new PluginRegistry(),
            workspaceModuleLauncher: (_, _) =>
            {
                launcherCalled = true;
                return Task.FromResult(PluginCommandResult.Success());
            });

        var result = await executor.ExecuteAsync(
            new SearchResultAction(
                SearchActionKind.OpenWorkspaceModule,
                "plugin-settings:unsafe/id"),
            ContextSnapshot.Empty);

        Assert.False(result.IsSuccess);
        Assert.False(launcherCalled);
        Assert.Contains("地址无效", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceActionWithoutLauncher_FailsClosed()
    {
        var executor = new SearchResultActionExecutor(new PluginRegistry());

        var result = await executor.ExecuteAsync(
            new SearchResultAction(
                SearchActionKind.OpenWorkspaceModule,
                "management:root"),
            ContextSnapshot.Empty);

        Assert.False(result.IsSuccess);
        Assert.Contains("不可用", result.Message);
    }

    [Fact]
    public async Task SuperPanelWorkspaceAction_HidesBeforeNavigationAndRecordsUse()
    {
        var preferences = new SearchPreferenceService(new MemoryStorage());
        var hidden = false;
        var hiddenBeforeLaunch = false;
        var coordinator = new SuperPanelActionCoordinator(
            new PluginRegistry(),
            preferences,
            workspaceModuleLauncher: (_, _) =>
            {
                hiddenBeforeLaunch = hidden;
                return Task.FromResult(PluginCommandResult.Success());
            });
        var selected = new SearchResultItem
        {
            Id = "workspace.management",
            ProviderId = "test",
            Title = "Management",
            PrimaryAction = new SearchResultAction(
                SearchActionKind.OpenWorkspaceModule,
                "management:root"),
        };

        var outcome = await coordinator.ExecuteAsync(
            selected,
            selected.PrimaryAction,
            ContextSnapshot.Empty,
            () =>
            {
                hidden = true;
                return Task.CompletedTask;
            });

        Assert.True(outcome.IsSuccess);
        Assert.True(hiddenBeforeLaunch);
        Assert.Contains(selected.Id, preferences.GetRecentResultIds());
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
            => Task.FromResult(
                HostApiResponse<bool>.Success(_values.ContainsKey(key)));
    }
}
