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

        var report = await registry.ShutdownAllAsync(
            TimeSpan.FromMilliseconds(40));

        Assert.True(released.WasDisposed);
        Assert.False(report.Completed);
        Assert.Equal(
            [PluginShutdownStatus.TimedOut, PluginShutdownStatus.Passed],
            report.Results.Select(result => result.Status));
        Assert.Equal(["stalled"], report.IncompletePluginIds);
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

        var report = await registry.ShutdownAllAsync(
            TimeSpan.FromMilliseconds(40));

        Assert.False(report.Completed);
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

        var report = await registry.ShutdownAllAsync(TimeSpan.FromSeconds(1));

        Assert.True(plugin.WasDisposed);
        Assert.True(report.Completed);
    }

    [Fact]
    public async Task ShutdownAllAsync_TotalBudgetBoundsMultipleStallsAndReportsEveryPlugin()
    {
        var registry = new PluginRegistry();
        var first = new StalledPlugin();
        var second = new StalledPlugin();
        var skipped = new ReleasablePlugin();
        Register(registry, "first", first, PluginState.Running);
        Register(registry, "second", second, PluginState.Running);
        Register(registry, "skipped", skipped, PluginState.Loaded);

        var report = await registry.ShutdownAllAsync(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(140));

        Assert.False(report.Completed);
        Assert.Equal(3, report.Results.Count);
        Assert.Equal(
            [
                PluginShutdownStatus.TimedOut,
                PluginShutdownStatus.TimedOut,
                PluginShutdownStatus.SkippedTotalBudget,
            ],
            report.Results.Select(result => result.Status));
        Assert.Equal(
            ["first", "second", "skipped"],
            report.IncompletePluginIds);
        Assert.False(skipped.WasDisposed);
        Assert.True(report.ElapsedMilliseconds < 500);
        Assert.Equal(140, report.TotalBudgetMilliseconds);
        Assert.Equal(100, report.Results[0].WaitBudgetMilliseconds);
        Assert.InRange(
            report.Results[1].WaitBudgetMilliseconds!.Value,
            1,
            100);
        first.AllowStop();
        second.AllowStop();
    }

    [Fact]
    public async Task ShutdownAllAsync_NormalCatalogCompletesWithinTotalBudget()
    {
        var registry = new PluginRegistry();
        var plugins = Enumerable.Range(1, 25)
            .Select(_ => new ReleasablePlugin())
            .ToArray();
        for (var index = 0; index < plugins.Length; index++)
        {
            Register(
                registry,
                $"plugin-{index + 1}",
                plugins[index],
                PluginState.Loaded);
        }

        var report = await registry.ShutdownAllAsync(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(2));

        Assert.True(report.Completed);
        Assert.Equal(25, report.Results.Count);
        Assert.Empty(report.IncompletePluginIds);
        Assert.All(report.Results, result =>
            Assert.Equal(PluginShutdownStatus.Passed, result.Status));
        Assert.All(plugins, plugin => Assert.True(plugin.WasDisposed));
        Assert.True(report.ElapsedMilliseconds < 2_000);
    }

    [Fact]
    public async Task ShutdownAllAsync_RejectsNonPositiveTotalBudget()
    {
        var registry = new PluginRegistry();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            registry.ShutdownAllAsync(
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero));
    }

    [Fact]
    public async Task ShutdownAllForHostAsync_SurfacesIncompleteShutdownToTopLevel()
    {
        var registry = new PluginRegistry();
        var stalled = new StalledPlugin();
        Register(registry, "stalled", stalled, PluginState.Running);

        var exception = await Assert.ThrowsAsync<IncompletePluginShutdownException>(
            async () => await registry.ShutdownAllForHostAsync(
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(80)));

        Assert.Equal(["stalled"], exception.IncompletePluginIds);
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
