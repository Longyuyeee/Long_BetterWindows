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
}
