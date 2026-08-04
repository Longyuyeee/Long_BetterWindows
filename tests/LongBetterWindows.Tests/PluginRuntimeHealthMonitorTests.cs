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

    private sealed class HealthCommandPlugin : ILongPlugin, IPluginCommandHandler
    {
        public string Outcome { get; set; } = "success";
        public string Id => "health.sample";
        public string Name => "Health sample";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
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
            => Outcome switch
            {
                "failure" => Task.FromResult(PluginCommandResult.Failure("expected")),
                "exception" => throw new InvalidOperationException("sensitive detail"),
                _ => Task.FromResult(PluginCommandResult.Success()),
            };
    }
}
