using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class PluginRuntimeHealthMonitorTests
{
    [Fact]
    public void Snapshot_TransitionsFromIdleThroughFailureAndRecovery()
    {
        var monitor = new PluginRuntimeHealthMonitor();

        Assert.Equal(
            PluginRuntimeHealthState.Idle,
            monitor.GetSnapshot("sample").State);

        monitor.RecordFailure("sample", TimeSpan.FromMilliseconds(20));
        var degraded = monitor.GetSnapshot("sample");
        Assert.Equal(PluginRuntimeHealthState.Degraded, degraded.State);
        Assert.Equal(1, degraded.FailureCount);
        Assert.Equal(PluginRuntimeFailureKind.CommandFailed, degraded.LastFailureKind);

        monitor.RecordException("sample", TimeSpan.FromMilliseconds(30));
        Assert.Equal(
            PluginRuntimeHealthState.Unhealthy,
            monitor.GetSnapshot("sample").State);
        monitor.RecordLifecycleTransition(
            "sample",
            PluginRuntimeLifecycleState.Stopped);
        Assert.Equal(
            PluginRuntimeHealthState.Unhealthy,
            monitor.GetSnapshot("sample").State);
        monitor.RecordLifecycleTransition(
            "sample",
            PluginRuntimeLifecycleState.Running);
        Assert.Equal(
            PluginRuntimeHealthState.Healthy,
            monitor.GetSnapshot("sample").State);

        monitor.RecordSuccess("sample", TimeSpan.FromMilliseconds(10));
        var recovered = monitor.GetSnapshot("sample");
        Assert.Equal(PluginRuntimeHealthState.Healthy, recovered.State);
        Assert.Equal(3, recovered.ExecutionCount);
        Assert.Equal(1, recovered.SuccessCount);
        Assert.Equal(2, recovered.FailureCount);
        Assert.Equal(1, recovered.ExceptionCount);
        Assert.Equal(0, recovered.ConsecutiveFailureCount);
        Assert.Equal(PluginRuntimeFailureKind.None, recovered.LastFailureKind);
        Assert.Equal(30, recovered.MaximumDurationMilliseconds);
    }

    [Fact]
    public void Snapshot_CancellationDoesNotDegradePluginHealth()
    {
        var monitor = new PluginRuntimeHealthMonitor();

        monitor.RecordSuccess("sample", TimeSpan.FromMilliseconds(5));
        monitor.RecordCancellation("sample", TimeSpan.FromMilliseconds(8));

        var snapshot = monitor.GetSnapshot("sample");
        Assert.Equal(PluginRuntimeHealthState.Healthy, snapshot.State);
        Assert.Equal(2, snapshot.ExecutionCount);
        Assert.Equal(1, snapshot.CancellationCount);
        Assert.Equal(0, snapshot.FailureCount);
    }

    [Fact]
    public async Task PluginRegistry_RecordsSuccessfulLifecycleTransitions()
    {
        var registry = new PluginRegistry();
        var plugin = new HealthCommandPlugin();
        registry.Register(Manifest(), plugin, null, AppContext.BaseDirectory);

        Assert.Equal(
            PluginRuntimeLifecycleState.Loaded,
            registry.RuntimeHealth.GetSnapshot(plugin.Id).LifecycleState);
        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.True(await registry.StopPluginAsync(plugin.Id));
        Assert.True(registry.Unregister(plugin.Id));

        var snapshot = registry.RuntimeHealth.GetSnapshot(plugin.Id);
        Assert.Equal(PluginRuntimeLifecycleState.Unloaded, snapshot.LifecycleState);
        Assert.Equal(4, snapshot.LifecycleEventCount);
        Assert.Equal(0, snapshot.LifecycleFailureCount);
        Assert.Equal(PluginRuntimeHealthState.Idle, snapshot.State);
    }

    [Fact]
    public void Snapshot_ConcurrentUpdatesAreCompleteAndSorted()
    {
        var monitor = new PluginRuntimeHealthMonitor();

        Parallel.For(0, 200, index =>
            monitor.RecordSuccess(
                index % 2 == 0 ? "plugin.b" : "plugin.a",
                TimeSpan.FromMilliseconds(index)));

        var snapshots = monitor.GetAllSnapshots();
        Assert.Equal(["plugin.a", "plugin.b"], snapshots.Select(item => item.PluginId));
        Assert.All(snapshots, snapshot => Assert.Equal(100, snapshot.SuccessCount));
        Assert.All(snapshots, snapshot => Assert.Equal(100, snapshot.ExecutionCount));
    }

    [Fact]
    public async Task CommandExecutor_RecordsPluginResultAndUnhandledException()
    {
        var registry = new PluginRegistry();
        var plugin = new HealthCommandPlugin();
        registry.Register(Manifest(), plugin, null, AppContext.BaseDirectory);
        var executor = new CommandExecutor(registry);

        plugin.Outcome = "failure";
        Assert.False((await executor.ExecuteAsync("health.sample:run")).IsSuccess);
        Assert.Equal(
            PluginRuntimeHealthState.Degraded,
            registry.RuntimeHealth.GetSnapshot("health.sample").State);

        plugin.Outcome = "exception";
        Assert.False((await executor.ExecuteAsync("health.sample:run")).IsSuccess);
        var unhealthy = registry.RuntimeHealth.GetSnapshot("health.sample");
        Assert.Equal(PluginRuntimeHealthState.Unhealthy, unhealthy.State);
        Assert.Equal(1, unhealthy.ExceptionCount);

        plugin.Outcome = "success";
        Assert.True((await executor.ExecuteAsync("health.sample:run")).IsSuccess);
        Assert.Equal(
            PluginRuntimeHealthState.Healthy,
            registry.RuntimeHealth.GetSnapshot("health.sample").State);
    }

    [Fact]
    public async Task CommandExecutor_StartFailureIsRecordedOnceByLifecycleOwner()
    {
        var registry = new PluginRegistry();
        var plugin = new HealthCommandPlugin { StartSucceeds = false };
        registry.Register(Manifest(), plugin, null, AppContext.BaseDirectory);

        var result = await new CommandExecutor(registry).ExecuteAsync("health.sample:run");

        Assert.False(result.IsSuccess);
        var snapshot = registry.RuntimeHealth.GetSnapshot(plugin.Id);
        Assert.Equal(0, snapshot.ExecutionCount);
        Assert.Equal(1, snapshot.LifecycleFailureCount);
        Assert.Equal(PluginRuntimeFailureKind.StartFailed, snapshot.LastFailureKind);
        Assert.Equal(PluginRuntimeHealthState.Degraded, snapshot.State);
    }

    [Fact]
    public async Task PluginRegistry_ResourceReleaseFailureKeepsRunningState()
    {
        var registry = new PluginRegistry();
        var plugin = new HealthCommandPlugin { ReleaseThrows = true };
        registry.Register(Manifest(), plugin, null, AppContext.BaseDirectory);
        Assert.True(await registry.StartPluginAsync(plugin.Id));

        Assert.False(await registry.StopPluginAsync(plugin.Id));

        Assert.Equal(PluginState.Running, registry.Get(plugin.Id)!.State);
        var snapshot = registry.RuntimeHealth.GetSnapshot(plugin.Id);
        Assert.Equal(1, snapshot.LifecycleFailureCount);
        Assert.Equal(1, snapshot.ExceptionCount);
        Assert.Equal(
            PluginRuntimeFailureKind.ResourceReleaseFailed,
            snapshot.LastFailureKind);
        Assert.Equal(PluginRuntimeHealthState.Degraded, snapshot.State);
    }

    [Fact]
    public void Diagnostics_AreSortedAndRetainOnlyHealthAfterUnregister()
    {
        var registry = new PluginRegistry();
        registry.Register(Manifest(), new HealthCommandPlugin(), null, AppContext.BaseDirectory);
        registry.Register(new PluginManifest
        {
            Id = "alpha.plugin",
            Name = "Alpha",
            Version = "2.0.0",
            Runtime = "webview",
            EntryPoint = "index.html",
        }, new object(), null, AppContext.BaseDirectory);

        var active = PluginRuntimeDiagnostics.Build(registry);
        Assert.Equal(["alpha.plugin", "health.sample"], active.Select(item => item.PluginId));
        Assert.Equal("webview", active[0].Runtime);
        Assert.Equal("loaded", active[0].RegistryState);

        Assert.True(registry.Unregister("alpha.plugin"));
        var unloaded = PluginRuntimeDiagnostics.Build(registry)[0];
        Assert.Equal("alpha.plugin", unloaded.PluginId);
        Assert.Null(unloaded.Name);
        Assert.Null(unloaded.Version);
        Assert.Null(unloaded.Runtime);
        Assert.Equal("unloaded", unloaded.RegistryState);
        Assert.Equal(
            PluginRuntimeLifecycleState.Unloaded,
            unloaded.Health.LifecycleState);
    }

    [Fact]
    public void DiagnosticPresentation_PrioritizesFailuresAndBuildsAccessibleSummary()
    {
        var registry = new PluginRegistry();
        registry.Register(Manifest(), new HealthCommandPlugin(), null, AppContext.BaseDirectory);
        registry.Register(new PluginManifest
        {
            Id = "alpha.plugin",
            Name = "Alpha",
            Version = "2.0.0",
            Runtime = "webview",
            EntryPoint = "index.html",
        }, new object(), null, AppContext.BaseDirectory);
        registry.RuntimeHealth.RecordFailure(
            "health.sample",
            TimeSpan.FromMilliseconds(12));

        var rows = PluginRuntimeDiagnosticPresentation.Build(
            PluginRuntimeDiagnostics.Build(registry),
            key => key switch
            {
                "diagnostics.health.state.degraded" => "attention",
                "diagnostics.health.state.idle" => "idle",
                "diagnostics.health.registry.loaded" => "loaded",
                "diagnostics.health.summary" => "{0}/{1}/{2}",
                "diagnostics.health.itemA11y" => "{0}|{1}|{2}|{3}",
                _ => key,
            });

        Assert.Equal(["health.sample", "alpha.plugin"], rows.Select(row => row.PluginId));
        Assert.Equal("attention", rows[0].HealthState);
        Assert.Equal("1/1/0", rows[0].Summary);
        Assert.Equal(
            "Health sample|attention|loaded|1/1/0",
            rows[0].AccessibilityName);
        Assert.Equal("webview · v2.0.0", rows[1].Identity);
    }

    private static PluginManifest Manifest() => new()
    {
        Id = "health.sample",
        Name = "Health sample",
        Version = "1.0.0",
        EntryPoint = "health.sample.dll",
        Commands =
        [
            new PluginCommand
            {
                Id = "run",
                Title = "Run",
                AcceptedInputs = [AcceptedInputType.None],
            },
        ],
    };

    private sealed class HealthCommandPlugin :
        ILongPlugin,
        IPluginCommandHandler,
        IPluginResourceLifecycle
    {
        public string Outcome { get; set; } = "success";
        public bool StartSucceeds { get; set; } = true;
        public bool ReleaseThrows { get; set; }
        public string Id => "health.sample";
        public string Name => "Health sample";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync()
        {
            if (StartSucceeds) State = PluginState.Running;
            return Task.FromResult(StartSucceeds);
        }
        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }
        public Task ReleaseResourcesAsync()
            => ReleaseThrows
                ? Task.FromException(new InvalidOperationException("sensitive release detail"))
                : Task.CompletedTask;

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
            => Outcome switch
            {
                "failure" => Task.FromResult(PluginCommandResult.Failure("expected")),
                "exception" => throw new InvalidOperationException("sensitive detail"),
                _ => Task.FromResult(PluginCommandResult.Success()),
            };
    }
}
