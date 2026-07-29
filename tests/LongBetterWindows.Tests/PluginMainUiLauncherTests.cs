using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class PluginMainUiLauncherTests
{
    [Fact]
    public async Task OpenAsync_StartsLoadedPluginWithoutPersistingAutoStart()
    {
        var registry = new PluginRegistry();
        var plugin = new MainUiPlugin();
        Register(registry, plugin);

        var result = await PluginMainUiLauncher.OpenAsync(registry, plugin.Id);

        Assert.Equal(PluginMainUiOpenStatus.Opened, result);
        Assert.Equal(1, plugin.StartCount);
        Assert.Equal(1, plugin.ShowCount);
        Assert.Null(registry.Get(plugin.Id)!.GetSetting("auto_start"));
    }

    [Fact]
    public async Task OpenAsync_OpensBackgroundPluginWithoutStartingAgain()
    {
        var registry = new PluginRegistry();
        var plugin = new MainUiPlugin();
        Register(registry, plugin);
        Assert.True(await registry.StartPluginAsync(
            plugin.Id,
            persistAutoStart: false));
        Assert.True(await registry.MoveToBackgroundAsync(plugin.Id));

        var result = await PluginMainUiLauncher.OpenAsync(registry, plugin.Id);

        Assert.Equal(PluginMainUiOpenStatus.Opened, result);
        Assert.Equal(1, plugin.StartCount);
        Assert.Equal(0, plugin.ResumeCount);
        Assert.Equal(1, plugin.ShowCount);
        Assert.Equal(PluginState.Background, registry.Get(plugin.Id)!.State);
    }

    [Fact]
    public async Task OpenAsync_ReturnsExplicitFailureStatuses()
    {
        var registry = new PluginRegistry();
        var failedPlugin = new MainUiPlugin { StartSucceeds = false };
        Register(registry, failedPlugin);

        Assert.Equal(
            PluginMainUiOpenStatus.PluginMissing,
            await PluginMainUiLauncher.OpenAsync(registry, "missing"));
        Assert.Equal(
            PluginMainUiOpenStatus.StartFailed,
            await PluginMainUiLauncher.OpenAsync(registry, failedPlugin.Id));
        Assert.Equal(0, failedPlugin.ShowCount);
    }

    private static void Register(PluginRegistry registry, MainUiPlugin plugin)
    {
        Assert.True(registry.Register(
            new PluginManifest
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Version = plugin.Version,
                EntryPoint = "main-ui.dll",
                Lifecycle = new PluginLifecyclePreference
                {
                    CloseBehavior = PluginCloseBehavior.Background,
                },
            },
            plugin,
            null,
            "/main-ui"));
    }

    private sealed class MainUiPlugin :
        ILongPlugin,
        IHasMainUI,
        IPluginBackgroundLifecycle
    {
        public string Id => "main-ui";
        public string Name => "Main UI";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public bool StartSucceeds { get; init; } = true;
        public int StartCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int ShowCount { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);

        public Task<bool> StartAsync()
        {
            StartCount++;
            if (StartSucceeds)
                State = PluginState.Running;
            return Task.FromResult(StartSucceeds);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task<bool> EnterBackgroundAsync()
        {
            State = PluginState.Background;
            return Task.FromResult(true);
        }

        public Task<bool> ResumeAsync()
        {
            ResumeCount++;
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public void ShowMainUI() => ShowCount++;
    }
}
