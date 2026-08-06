using System.Diagnostics;
using System.IO;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class ExperimentalPluginWorkerTests
{
    [Fact]
    public async Task Worker_AuthenticatesRunsLifecycleCommandsAndReportsOwnResources()
    {
        await using var session = await StartWorkerAsync();
        var initial = session.CaptureResourceSnapshot();
        Assert.Equal(session.ProcessId, initial.ProcessId);
        Assert.False(initial.HasExited);
        Assert.True(initial.WorkingSetBytes > 0);

        Assert.Equal("initialized", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Initialize)).State);
        Assert.Equal("running", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Start)).State);
        var echoed = await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("echo", "hello"));
        Assert.Equal("hello", echoed.Text);
        Assert.Equal("running", echoed.State);

        var beforeBurn = session.CaptureResourceSnapshot();
        await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("burn", "done", 150));
        var afterBurn = session.CaptureResourceSnapshot();
        Assert.True(afterBurn.TotalProcessorMilliseconds > beforeBurn.TotalProcessorMilliseconds);
        Assert.Equal("background", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.EnterBackground)).State);
        Assert.Equal("running", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Resume)).State);
        Assert.Equal("running", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.LanguageChanged, "en-US")).State);
        Assert.Equal("stopped", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Stop)).State);
        Assert.Equal("released", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.ReleaseResources)).State);
    }

    [Fact]
    public async Task Worker_CancelsAndTimesOutCommandsWithoutLosingSession()
    {
        await using var session = await StartRunningWorkerAsync();
        var requestId = Guid.NewGuid().ToString();
        var invocation = session.InvokeCommandWithIdAsync(
            requestId,
            new PluginWorkerCommandRequest("delay", "late", 5_000));
        await Task.Delay(100);

        var cancellation = await session.CancelCommandAsync(requestId);
        Assert.True(cancellation.Cancelled);
        var cancelled = await Assert.ThrowsAsync<IpcRemoteException>(
            async () => await invocation);
        Assert.Equal(IpcErrorCodes.Cancelled, cancelled.Code);

        var timedOut = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            session.InvokeCommandAsync(
                new PluginWorkerCommandRequest("delay", "late", 1_000),
                deadlineMilliseconds: 100));
        Assert.Equal(IpcErrorCodes.Timeout, timedOut.Code);
        Assert.Equal("alive", (await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("echo", "alive"))).Text);
    }

    [Fact]
    public async Task Worker_CrashFailsPendingRequestAndExposesExitedSnapshot()
    {
        await using var session = await StartRunningWorkerAsync();
        var crashed = await Assert.ThrowsAsync<PluginWorkerExitedException>(() =>
            session.InvokeCommandAsync(new PluginWorkerCommandRequest("crash")));
        Assert.Equal(91, crashed.ExitCode);
        using var timeout = new CancellationTokenSource(5_000);
        await session.WaitForExitAsync(timeout.Token);
        var snapshot = session.CaptureResourceSnapshot();
        Assert.True(snapshot.HasExited);
        Assert.Equal(session.ProcessId, snapshot.ProcessId);
    }

    [Fact]
    public void Handshake_RequiresSpawnedProcessIdentityPluginAndNonce()
    {
        Assert.False(typeof(ExperimentalPluginWorkerSession).IsPublic);
        Assert.False(typeof(PluginWorkerProtocol).IsPublic);
        Assert.DoesNotContain(
            typeof(IpcProtocol).Assembly.ExportedTypes,
            type => type.Name.Contains("PluginWorker", StringComparison.Ordinal)
                || type.Name.Contains("ExperimentalPluginWorker", StringComparison.Ordinal));
        var request = new PluginWorkerHelloRequest("plugin.test", "nonce", 42);
        Assert.True(PluginWorkerHandshake.IsValid(
            "plugin.test", "nonce", 42, request, 42));
        Assert.False(PluginWorkerHandshake.IsValid(
            "plugin.other", "nonce", 42, request, 42));
        Assert.False(PluginWorkerHandshake.IsValid(
            "plugin.test", "wrong", 42, request, 42));
        Assert.False(PluginWorkerHandshake.IsValid(
            "plugin.test", "nonce", 42, request, 43));

        var repositoryRoot = FindRepositoryRoot();
        var hostProject = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "LongBetterWindows.Host", "LongBetterWindows.Host.csproj"));
        var workerProject = File.ReadAllText(Path.Combine(
            repositoryRoot, "tools", "LongBetterWindows.PluginWorker",
            "LongBetterWindows.PluginWorker.csproj"));
        Assert.DoesNotContain("LongBetterWindows.PluginWorker", hostProject, StringComparison.Ordinal);
        Assert.DoesNotContain("LongBetterWindows.Host", workerProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<UseWPF>true", workerProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Worker_DisposeTerminatesTheDedicatedProcess()
    {
        var session = await StartWorkerAsync();
        var processId = session.ProcessId;
        await session.DisposeAsync();

        await AssertEventuallyAsync(() =>
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        });
    }

    [Fact]
    public void HealthBridge_SeparatesCancellationTimeoutCrashAndLifecycle()
    {
        var health = new PluginRuntimeHealthMonitor();
        const string pluginId = "synthetic.headless.native";
        PluginWorkerHealthBridge.RecordLifecycleState(
            health, pluginId, "running");
        PluginWorkerHealthBridge.RecordCommandOutcome(
            health,
            pluginId,
            TimeSpan.FromMilliseconds(10),
            new IpcRemoteException(IpcErrorCodes.Cancelled, "cancelled", false));
        PluginWorkerHealthBridge.RecordCommandOutcome(
            health,
            pluginId,
            TimeSpan.FromMilliseconds(100),
            new IpcRemoteException(IpcErrorCodes.Timeout, "timeout", false));

        var degraded = health.GetSnapshot(pluginId);
        Assert.Equal(PluginRuntimeHealthState.Degraded, degraded.State);
        Assert.Equal(1, degraded.CancellationCount);
        Assert.Equal(PluginRuntimeFailureKind.WorkerTimeout, degraded.LastFailureKind);

        PluginWorkerHealthBridge.RecordCommandOutcome(
            health,
            pluginId,
            TimeSpan.FromMilliseconds(20),
            new PluginWorkerExitedException(91));
        var crashed = health.GetSnapshot(pluginId);
        Assert.Equal(PluginRuntimeHealthState.Unhealthy, crashed.State);
        Assert.Equal(1, crashed.ExceptionCount);
        Assert.Equal(PluginRuntimeFailureKind.WorkerCrashed, crashed.LastFailureKind);
    }

    private static async Task<ExperimentalPluginWorkerSession> StartRunningWorkerAsync()
    {
        var session = await StartWorkerAsync();
        try
        {
            await session.InvokeLifecycleAsync(PluginWorkerLifecycleOperation.Initialize);
            await session.InvokeLifecycleAsync(PluginWorkerLifecycleOperation.Start);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static Task<ExperimentalPluginWorkerSession> StartWorkerAsync()
        => ExperimentalPluginWorkerSession.StartAsync(WorkerPath());

    private static async Task AssertEventuallyAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.True(condition(), "Condition did not become true before the timeout.");
    }

    private static string WorkerPath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name ?? "Release";
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "LongBetterWindows.PluginWorker",
            "bin",
            configuration,
            "net8.0-windows",
            "long-plugin-worker.dll");
        Assert.True(File.Exists(path), $"Synthetic plugin worker is missing: {path}");
        return path;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
