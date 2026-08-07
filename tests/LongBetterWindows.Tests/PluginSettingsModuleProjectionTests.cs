using System.IO;
using System.Text.Json;
using System.Windows;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class PluginSettingsModuleProjectionTests
{
    [Fact]
    public void Build_ProjectsSettingsRuntimePermissionsAndUsageWithoutStarting()
    {
        var entry = Entry(
            "settings",
            new SettingsProvider(),
            PluginState.Loaded);
        var usage = new PluginUsageStats
        {
            PluginId = entry.Id,
            TotalCalls = 7,
            LastCallTime = new DateTime(2026, 7, 28, 12, 0, 0),
        };

        var state = PluginSettingsModuleProjection.Build(
            entry,
            usage,
            key => $"localized:{key}");

        Assert.True(state.HasSettings);
        Assert.True(state.CanOpen);
        Assert.False(state.IsRunning);
        Assert.Equal(PluginState.Loaded, entry.State);
        Assert.Equal(2, state.CapabilityCount);
        Assert.Equal(7, state.TotalCalls);
        Assert.Equal("Ctrl+Alt+S", state.Hotkey);
        Assert.Equal("DLL", state.RuntimeLabel);
    }

    [Fact]
    public void Build_AllowsGenericDetailsForPluginWithoutCustomSettings()
    {
        var entry = Entry(
            "plain",
            new object(),
            PluginState.Running);

        var state = PluginSettingsModuleProjection.Build(entry);

        Assert.False(state.HasSettings);
        Assert.True(state.IsRunning);
        Assert.True(state.CanOpen);
        Assert.Equal(PluginState.Running, entry.State);
    }

    [Fact]
    public async Task BuildCommands_ProjectsOnlyPluginCommandsWithSharedPreferences()
    {
        var entry = Entry("commands", new object(), PluginState.Loaded);
        var other = Entry("other", new object(), PluginState.Loaded);
        var registry = new CommandRegistry();
        registry.RegisterManifest(entry.Manifest);
        registry.RegisterManifest(other.Manifest);
        var descriptor = registry.Get("commands:commands.open")! with
        {
            Title = "Localized command",
        };
        var pinnedId = CommandSearchResultIdentity.BuildResultId(descriptor.Key);
        var commandPreferences = new CommandPreferenceService(new MemoryStorage());
        await commandPreferences.SetEnabledAsync(descriptor.Key, false);
        await commandPreferences.SetAliasesAsync(descriptor.Key, ["my alias"]);

        var items = PluginSettingsModuleProjection.BuildCommands(
            entry,
            registry.GetAll().Select(item => item.Key == descriptor.Key
                ? descriptor
                : item),
            [pinnedId],
            key => key switch
            {
                "plugins.command.aliases" => "Aliases: {0}",
                "plugins.command.inputs" => "Inputs: {0}",
                "plugins.command.input.text" => "Localized text",
                "plugins.command.input.file" => "Localized file",
                "action.unpin" => "Remove pin",
                _ => key,
            },
            commandPreferences);

        var item = Assert.Single(items);
        Assert.Equal("Localized command", item.Title);
        Assert.Equal("command:commands:commands.open", item.ResultId);
        Assert.Equal("Aliases: open · launch", item.AliasSummary);
        Assert.Equal("Inputs: Localized text · Localized file", item.InputSummary);
        Assert.True(item.IsPinned);
        Assert.Equal("Remove pin", item.PinText);
        Assert.False(item.IsEnabled);
        Assert.Equal("my alias", item.CustomAliasesText);
        Assert.Equal(string.Empty, item.HotkeyText);
        Assert.False(item.CanClearHotkey);
    }

    private static PluginEntry Entry(
        string id,
        object instance,
        PluginState state)
        => new(
            new PluginManifest
            {
                Id = id,
                Name = id,
                Version = "1.2.0",
                EntryPoint = $"{id}.dll",
                Capabilities = ["system.hotkey", "storage.local"],
                DefaultSettings = new Dictionary<string, object>
                {
                    ["hotkey"] = JsonDocument
                        .Parse("\"Ctrl+Alt+S\"")
                        .RootElement
                        .Clone(),
                },
                Commands =
                [
                    new PluginCommand
                    {
                        Id = $"{id}.open",
                        Title = $"Open {id}",
                        Description = $"Open {id} content",
                        Aliases = ["open", "launch", "OPEN"],
                        AcceptedInputs =
                        [
                            AcceptedInputType.Text,
                            AcceptedInputType.File,
                            AcceptedInputType.Text,
                        ],
                    },
                ],
            },
            instance,
            Path.GetTempPath(),
            registrationRevision: 3)
        {
            State = state,
        };

    private sealed class SettingsProvider : IHasSettingsUI, IHasMainUI
    {
        public FrameworkElement CreateSettingsUI()
            => throw new NotSupportedException();

        public void ShowMainUI()
        {
        }
    }

    private sealed class MemoryStorage : LongBetterWindows.Host.Capabilities.IStorageService
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
            => Task.FromResult(HostApiResponse<bool>.Success(
                _values.ContainsKey(key)));
    }
}
