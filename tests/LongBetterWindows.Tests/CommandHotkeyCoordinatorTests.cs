using System.Windows.Threading;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandHotkeyCoordinatorTests
{
    [Fact]
    public async Task Change_PersistsStableIdentityAndReportsConflictOwner()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F10");
        await fixture.Hotkeys.RegisterAsync(
            "Ctrl+Alt+F11",
            "other.owner",
            () => { });

        var conflict = await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F11");

        Assert.True(first.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal(ApiErrorCode.HotKeyConflict, conflict.ErrorCode);
        Assert.Equal("other.owner", conflict.ConflictOwner);
        Assert.Equal(
            CommandHotkeyCoordinator.BuildOwner(fixture.CommandKey),
            fixture.Hotkeys.GetOwner("Ctrl+Alt+F10"));
        var persisted = Assert.Single(fixture.Storage.Values).Value;
        Assert.Contains(fixture.CommandKey, persisted);
        Assert.DoesNotContain("Open demo", persisted);
    }

    [Fact]
    public async Task PersistenceFailure_RollsRuntimeRegistrationBackToOldHotkey()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F10")).IsSuccess);
        fixture.Storage.FailWrites = true;

        var result = await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F11");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            CommandHotkeyCoordinator.BuildOwner(fixture.CommandKey),
            fixture.Hotkeys.GetOwner("Ctrl+Alt+F10"));
        Assert.Null(fixture.Hotkeys.GetOwner("Ctrl+Alt+F11"));
        Assert.Equal(
            "Ctrl+Alt+F10",
            fixture.Coordinator.GetState(fixture.CommandKey).Hotkey);
    }

    [Fact]
    public async Task RemovePersistenceFailure_RestoresRuntimeRegistration()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F10")).IsSuccess);
        fixture.Storage.FailWrites = true;

        var result = await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            null);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            CommandHotkeyCoordinator.BuildOwner(fixture.CommandKey),
            fixture.Hotkeys.GetOwner("Ctrl+Alt+F10"));
        Assert.Equal(
            "Ctrl+Alt+F10",
            fixture.Coordinator.GetState(fixture.CommandKey).Hotkey);
    }

    [Fact]
    public async Task DisabledCommand_ReleasesAndReenablingRestoresConfiguredHotkey()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Coordinator.ChangeAsync(
            fixture.CommandKey,
            "Ctrl+Alt+F10")).IsSuccess);

        await fixture.Preferences.SetEnabledAsync(fixture.CommandKey, false);
        await fixture.Coordinator.RefreshCommandAsync(fixture.CommandKey);

        var paused = fixture.Coordinator.GetState(fixture.CommandKey);
        Assert.True(paused.IsPaused);
        Assert.False(paused.IsRegistered);
        Assert.Equal("Ctrl+Alt+F10", paused.Hotkey);
        Assert.Null(fixture.Hotkeys.GetOwner("Ctrl+Alt+F10"));

        await fixture.Preferences.SetEnabledAsync(fixture.CommandKey, true);
        await fixture.Coordinator.RefreshCommandAsync(fixture.CommandKey);

        var restored = fixture.Coordinator.GetState(fixture.CommandKey);
        Assert.False(restored.IsPaused);
        Assert.True(restored.IsRegistered);
    }

    [Fact]
    public async Task Dispose_DeactivatesAndUnsubscribesCoordinator()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.True(fixture.Coordinator.IsActiveForQuality);
        Assert.True(fixture.Coordinator.IsSubscribedForQuality);

        fixture.Coordinator.Dispose();

        Assert.False(fixture.Coordinator.IsActiveForQuality);
        Assert.False(fixture.Coordinator.IsSubscribedForQuality);
    }

    private sealed class Fixture
    {
        public string CommandKey => "demo:demo.open";
        public required MemoryStorage Storage { get; init; }
        public required StubHotKeyService Hotkeys { get; init; }
        public required CommandPreferenceService Preferences { get; init; }
        public required CommandHotkeyCoordinator Coordinator { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var storage = new MemoryStorage();
            var hotkeys = new StubHotKeyService();
            var preferences = new CommandPreferenceService(storage);
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
                new CommandHandler(),
                null,
                "/demo");
            var coordinator = new CommandHotkeyCoordinator(
                storage,
                hotkeys,
                registry,
                new CommandExecutor(registry),
                Dispatcher.CurrentDispatcher);
            await coordinator.InitializeAsync();
            await coordinator.ActivateAsync();
            return new Fixture
            {
                Storage = storage,
                Hotkeys = hotkeys,
                Preferences = preferences,
                Coordinator = coordinator,
            };
        }
    }

    private sealed class CommandHandler : IPluginCommandHandler
    {
        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PluginCommandResult.Success());
    }

    private sealed class StubHotKeyService : IHotKeyService
    {
        private readonly Dictionary<string, Registration> _registered =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<HostApiResponse> RegisterAsync(string hotkey, Action callback)
            => RegisterAsync(hotkey, "builtin", callback);

        public Task<HostApiResponse> RegisterAsync(
            string hotkey,
            string pluginId,
            Action callback)
        {
            if (_registered.ContainsKey(hotkey))
            {
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.HotKeyConflict));
            }
            _registered[hotkey] = new Registration(pluginId, callback);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> UnregisterAsync(string hotkey)
        {
            return Task.FromResult(_registered.Remove(hotkey)
                ? HostApiResponse.Success()
                : HostApiResponse.Failure(ApiErrorCode.NotFound));
        }

        public Task<HostApiResponse<bool>> IsConflictAsync(string hotkey)
            => IsConflictAsync(hotkey, null);

        public Task<HostApiResponse<bool>> IsConflictAsync(
            string hotkey,
            string? excludedHotkey)
            => Task.FromResult(HostApiResponse<bool>.Success(
                _registered.ContainsKey(hotkey)
                && !string.Equals(
                    hotkey,
                    excludedHotkey,
                    StringComparison.OrdinalIgnoreCase)));

        public string? GetOwner(string hotkey)
            => _registered.TryGetValue(hotkey, out var registration)
                ? registration.Owner
                : null;

        public IReadOnlyDictionary<string, string> GetAllHotkeys()
            => _registered.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Owner,
                StringComparer.OrdinalIgnoreCase);

        public Task<HostApiResponse> ChangeHotkeyAsync(
            string oldHotkey,
            string newHotkey,
            string pluginId,
            Action callback)
        {
            if (_registered.TryGetValue(newHotkey, out var conflict)
                && !string.Equals(newHotkey, oldHotkey, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.HotKeyConflict,
                    conflict.Owner));
            }
            if (_registered.TryGetValue(oldHotkey, out var old)
                && !string.Equals(old.Owner, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.InvalidArgument));
            }
            _registered.Remove(oldHotkey);
            _registered[newHotkey] = new Registration(pluginId, callback);
            return Task.FromResult(HostApiResponse.Success());
        }

        private sealed record Registration(string Owner, Action Callback);
    }

    private sealed class MemoryStorage : IStorageService
    {
        public Dictionary<string, string> Values { get; } = new();
        public bool FailWrites { get; set; }

        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(
                Values.TryGetValue(key, out var value) ? value : null));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            if (FailWrites)
            {
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.Unknown,
                    "Synthetic persistence failure."));
            }
            Values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            if (FailWrites)
            {
                return Task.FromResult(HostApiResponse.Failure(
                    ApiErrorCode.Unknown,
                    "Synthetic persistence failure."));
            }
            Values.Remove(key);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(
                Values.ContainsKey(key)));
    }
}
