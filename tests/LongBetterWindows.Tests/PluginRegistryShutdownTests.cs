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
}
