using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class PluginRegistryShutdownTests
{
    [Fact]
    public async Task ShutdownAllAsync_TimesOutStalledPluginAndContinues()
    {
        var registry = new PluginRegistry();
        var stalled = new StalledPlugin();
        var released = new ReleasablePlugin();
        Register(registry, "stalled", stalled, PluginState.Running);
        Register(registry, "released", released, PluginState.Loaded);

        await registry.ShutdownAllAsync(TimeSpan.FromMilliseconds(40));

        Assert.True(released.WasDisposed);
        var health = registry.RuntimeHealth.GetSnapshot("stalled");
        Assert.Equal(1, health.LifecycleFailureCount);
        Assert.Equal(PluginRuntimeFailureKind.ShutdownTimeout, health.LastFailureKind);
        stalled.AllowStop();
    }

    [Fact]
    public async Task ShutdownAllAsync_LateCompletionDoesNotMutateRegistryStateOrNotifyUi()
    {
        var registry = new PluginRegistry();
        var stalled = new StalledDisposablePlugin();
        Register(registry, "stalled", stalled, PluginState.Running);
        var notifications = 0;
        var hostReleaseCount = 0;
        registry.PluginsChanged += () => notifications++;
        registry.AttachHostResourceReleaser(_ =>
        {
            hostReleaseCount++;
            return Task.CompletedTask;
        });

        await registry.ShutdownAllAsync(TimeSpan.FromMilliseconds(40));

        Assert.Equal(PluginState.Running, registry.Get("stalled")!.State);
        Assert.Equal(0, notifications);
        Assert.Equal(1, hostReleaseCount);
        stalled.AllowStop();
        await stalled.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PluginState.Running, registry.Get("stalled")!.State);
        Assert.Equal(0, notifications);
        Assert.Equal(1, hostReleaseCount);
        var health = registry.RuntimeHealth.GetSnapshot("stalled");
        Assert.Equal(1, health.LifecycleFailureCount);
        Assert.Equal(PluginRuntimeFailureKind.ShutdownTimeout, health.LastFailureKind);
    }

    [Fact]
    public async Task ShutdownAllAsync_HostResourceFailureDoesNotSkipPluginDisposal()
    {
        var registry = new PluginRegistry();
        var plugin = new ReleasablePlugin();
        Register(registry, "releasable", plugin, PluginState.Loaded);
        registry.AttachHostResourceReleaser(_ =>
            Task.FromException(new InvalidOperationException("host release failed")));

        await registry.ShutdownAllAsync(TimeSpan.FromSeconds(1));

        Assert.True(plugin.WasDisposed);
    }

    private static void Register(
        PluginRegistry registry,
        string id,
        object instance,
        PluginState state)
    {
        Assert.True(registry.Register(
            new PluginManifest
            {
                Id = id,
                Name = id,
                Version = "1.0.0",
            },
            instance,
            null,
            Path.Combine(Path.GetTempPath(), id)));
        registry.Get(id)!.State = state;
    }

    private sealed class StalledPlugin : ILongPlugin
    {
        private readonly TaskCompletionSource<bool> _stop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "stalled";
        public string Name => "Stalled";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Running;

        public Task<bool> InitializeAsync(IHostApi host)
            => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => _stop.Task;
        public void AllowStop() => _stop.TrySetResult(true);
    }

    private sealed class ReleasablePlugin : IDisposable
    {
        public bool WasDisposed { get; private set; }
        public void Dispose() => WasDisposed = true;
    }

    private sealed class StalledDisposablePlugin : ILongPlugin, IDisposable
    {
        private readonly TaskCompletionSource<bool> _stop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public string Id => "stalled";
        public string Name => "Stalled";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Running;

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => _stop.Task;
        public void AllowStop() => _stop.TrySetResult(true);
        public void Dispose() => Disposed.TrySetResult();
    }
}
