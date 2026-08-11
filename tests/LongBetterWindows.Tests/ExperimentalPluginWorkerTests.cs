using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class ExperimentalPluginWorkerTests
{
    [Fact]
    public async Task ReferenceWorkload_LoadsBoundedContractWithoutSyntheticFallback()
    {
        await using var session = await StartWorkerAsync(
            "reference.headless.native",
            workloadPolicy: ReferenceWorkloadPolicy());
        Assert.Equal("initialized", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Initialize)).State);
        Assert.Equal("running", (await session.InvokeLifecycleAsync(
            PluginWorkerLifecycleOperation.Start)).State);

        var digest = await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("reference.sha256", "hello"));
        Assert.Equal(
            "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824",
            digest.Text);

        var synthetic = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            session.InvokeCommandAsync(new PluginWorkerCommandRequest("echo", "blocked")));
        Assert.Equal(IpcErrorCodes.CommandNotFound, synthetic.Code);

        var requestId = Guid.NewGuid().ToString();
        var pending = session.InvokeCommandWithIdAsync(
            requestId,
            new PluginWorkerCommandRequest("reference.delay", "cancelled", 5_000));
        await Task.Delay(100);
        Assert.True((await session.CancelCommandAsync(requestId)).Cancelled);
        var cancelled = await Assert.ThrowsAsync<IpcRemoteException>(async () => await pending);
        Assert.Equal(IpcErrorCodes.Cancelled, cancelled.Code);

        var timedOut = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            session.InvokeCommandAsync(
                new PluginWorkerCommandRequest("reference.delay", "late", 1_000),
                deadlineMilliseconds: 100));
        Assert.Equal(IpcErrorCodes.Timeout, timedOut.Code);
        Assert.Equal("alive", (await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("reference.delay", "alive", 1))).Text);
    }

    [Fact]
    public void ReferenceWorkload_RemainsHeadlessAndOutsideAuthoritativeCatalog()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "LongBetterWindows.PluginWorker.Reference",
            "LongBetterWindows.PluginWorker.Reference.csproj"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "LongBetterWindows.PluginWorker.Reference",
            "ReferenceWorkload.cs"));
        var catalog = File.ReadAllText(Path.Combine(root, "catalog", "plugin-catalog.json"));

        Assert.DoesNotContain("LongBetterWindows.Host", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<UseWPF>true", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reference.headless.native", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkloadPolicy_LocksVerifiedBytesAndExactHostMethodBoundary()
    {
        var root = FindRepositoryRoot();
        var policySource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.PluginIpc",
            "Client",
            "PluginWorkerWorkloadLaunchPolicy.cs"));
        var workerSource = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "LongBetterWindows.PluginWorker",
            "Program.cs"));

        Assert.Contains("Path.GetRelativePath", policySource, StringComparison.Ordinal);
        Assert.Contains("FileAttributes.ReparsePoint", policySource, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", policySource, StringComparison.Ordinal);
        Assert.Contains("MaximumAssemblyBytes", policySource, StringComparison.Ordinal);
        Assert.Contains("AssemblyLoadContext.Default.LoadFromStream", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadFromAssemblyPath", workerSource, StringComparison.Ordinal);
        Assert.Contains("RequiredHostMethods.SetEquals", workerSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceWorkload_RejectsIdentityMismatchAfterHandshake()
    {
        await using var session = await StartWorkerAsync(
            "reference.wrong.identity",
            workloadPolicy: ReferenceWorkloadPolicy());
        using var timeout = new CancellationTokenSource(15_000);
        var rejected = await Record.ExceptionAsync(() =>
            session.InvokeLifecycleAsync(
                PluginWorkerLifecycleOperation.Initialize,
                cancellationToken: timeout.Token));
        Assert.True(
            rejected is PluginWorkerExitedException or IOException,
            $"Expected the mismatched workload connection to close, got {rejected?.GetType().Name ?? "success"}.");
    }

    [Fact]
    public async Task WorkloadPolicy_RejectsHashRootAndUnknownHostMethodBeforeSpawn()
    {
        var valid = ReferenceWorkloadPolicy();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExperimentalPluginWorkerSession.StartAsync(
                WorkerPath(),
                "reference.headless.native",
                workloadPolicy: valid with { ExpectedSha256 = new string('0', 64) }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExperimentalPluginWorkerSession.StartAsync(
                WorkerPath(),
                "reference.headless.native",
                workloadPolicy: valid with
                {
                    PackageRoot = Path.Combine(valid.PackageRoot, "nested"),
                }));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExperimentalPluginWorkerSession.StartAsync(
                WorkerPath(),
                "reference.headless.native",
                workloadPolicy: valid with
                {
                    AllowedHostMethods = new HashSet<string>(
                        ["host.unbounded"], StringComparer.Ordinal),
                }));
    }

    [Fact]
    public async Task ReferenceWorkload_RejectsHostMethodPolicyMismatchAfterHandshake()
    {
        var policy = ReferenceWorkloadPolicy() with
        {
            AllowedHostMethods = new HashSet<string>(
                [PluginWorkerProtocol.HostCapabilityQuery], StringComparer.Ordinal),
        };
        await using var session = await StartWorkerAsync(
            "reference.headless.native",
            workloadPolicy: policy);
        using var timeout = new CancellationTokenSource(15_000);
        var rejected = await Record.ExceptionAsync(() =>
            session.InvokeLifecycleAsync(
                PluginWorkerLifecycleOperation.Initialize,
                cancellationToken: timeout.Token));
        Assert.True(rejected is PluginWorkerExitedException or IOException);
    }

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
    public async Task Worker_QueriesCapabilitiesThroughBoundPluginContext()
    {
        const string pluginId = "synthetic.capability.reader";
        string? observedPluginId = null;
        var bridge = new PluginWorkerHostBridge(pluginId, capability =>
        {
            observedPluginId = PluginAccessContext.CurrentPluginId;
            return capability == "system.theme";
        });
        PluginAccessContext.CurrentPluginId = "outer.context";
        try
        {
            await using var session = await StartRunningWorkerAsync(pluginId, bridge);
            var granted = await session.InvokeCommandAsync(
                new PluginWorkerCommandRequest("query-capability", "system.theme"));
            Assert.Equal("granted", granted.Text);
            Assert.Equal(pluginId, observedPluginId);
            Assert.Equal("outer.context", PluginAccessContext.CurrentPluginId);

            var denied = await session.InvokeCommandAsync(
                new PluginWorkerCommandRequest("query-capability", "system.clipboard"));
            Assert.Equal("denied", denied.Text);
            Assert.Equal("outer.context", PluginAccessContext.CurrentPluginId);
        }
        finally
        {
            PluginAccessContext.CurrentPluginId = null;
        }
    }

    [Fact]
    public async Task HostBridge_RejectsUnknownMethodsAndRestoresContextOnFailure()
    {
        var bridge = new PluginWorkerHostBridge(
            "synthetic.capability.reader",
            _ => throw new InvalidOperationException("synthetic failure"));
        var unknown = await bridge.HandleRequestAsync(
            IpcEnvelope.RequestForProtocol(
                PluginWorkerProtocol.Name, "host.unknown", new { }),
            CancellationToken.None);
        Assert.Equal(IpcErrorCodes.SurfaceNotSupported, unknown.Error?.Code);

        PluginAccessContext.CurrentPluginId = "outer.context";
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bridge.HandleRequestAsync(
                    IpcEnvelope.RequestForProtocol(
                        PluginWorkerProtocol.Name,
                        PluginWorkerProtocol.HostCapabilityQuery,
                        new PluginWorkerCapabilityQueryRequest("system.theme")),
                    CancellationToken.None));
            Assert.Equal("outer.context", PluginAccessContext.CurrentPluginId);
        }
        finally
        {
            PluginAccessContext.CurrentPluginId = null;
            await bridge.DisposeAsync();
        }
    }

    [Fact]
    public async Task WorkerHostRequest_UsesItsOwnDeadline()
    {
        var bridge = new DeadlineObservingHostBridge();
        await using var session = await StartRunningWorkerAsync(
            "synthetic.capability.reader",
            bridge);
        var timeout = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            session.InvokeCommandAsync(
                new PluginWorkerCommandRequest(
                    "query-capability", "system.theme", 100),
                deadlineMilliseconds: 2_000));
        Assert.Equal(IpcErrorCodes.Timeout, timeout.Code);
        Assert.True(bridge.CancellationObserved.Task.IsCompletedSuccessfully);
        Assert.Equal("alive", (await session.InvokeCommandAsync(
            new PluginWorkerCommandRequest("echo", "alive"))).Text);
    }

    [Fact]
    public async Task WorkerCrash_ReleasesTrackedHostResourcesExactlyOnce()
    {
        var releases = new List<string>();
        var bridge = new PluginWorkerHostBridge(
            "synthetic.headless.native", _ => false);
        bridge.TrackResource(new RecordingResource("lease", releases));
        await using var session = await StartRunningWorkerAsync(
            "synthetic.headless.native", bridge);

        await Assert.ThrowsAsync<PluginWorkerExitedException>(() =>
            session.InvokeCommandAsync(new PluginWorkerCommandRequest("crash")));
        await AssertEventuallyAsync(() => releases.Count == 1);
        Assert.Equal(["lease"], releases);
        Assert.Equal(0, bridge.LeaseCount);
    }

    [Fact]
    public async Task ResourceLeaseScope_ReleasesLifoAndContinuesAfterFailure()
    {
        var releases = new List<string>();
        var leases = new PluginWorkerResourceLeaseScope();
        var first = leases.Acquire(new RecordingResource("first", releases));
        leases.Acquire(new RecordingResource("second", releases, fail: true));
        leases.Acquire(new RecordingResource("third", releases));

        Assert.True(await leases.ReleaseAsync(first));
        Assert.False(await leases.ReleaseAsync(first));
        var failure = await Assert.ThrowsAsync<AggregateException>(
            async () => await leases.DisposeAsync());
        Assert.Single(failure.InnerExceptions);
        Assert.Equal(["first", "third", "second"], releases);
        Assert.Equal(0, leases.Count);
        await leases.DisposeAsync();
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

    private static async Task<ExperimentalPluginWorkerSession> StartRunningWorkerAsync(
        string pluginId = "synthetic.headless.native",
        IExperimentalPluginWorkerHostBridge? hostBridge = null)
    {
        var session = await StartWorkerAsync(pluginId, hostBridge);
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

    private static Task<ExperimentalPluginWorkerSession> StartWorkerAsync(
        string pluginId = "synthetic.headless.native",
        IExperimentalPluginWorkerHostBridge? hostBridge = null,
        PluginWorkerWorkloadLaunchPolicy? workloadPolicy = null)
        => ExperimentalPluginWorkerSession.StartAsync(
            WorkerPath(),
            pluginId,
            hostBridge: hostBridge,
            workloadPolicy: workloadPolicy);

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

    private static string ReferenceWorkloadPath()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name ?? "Release";
        var path = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "LongBetterWindows.PluginWorker.Reference",
            "bin",
            configuration,
            "net8.0-windows",
            "long-plugin-worker-reference.dll");
        Assert.True(File.Exists(path), $"Reference worker workload is missing: {path}");
        return path;
    }

    private static PluginWorkerWorkloadLaunchPolicy ReferenceWorkloadPolicy()
    {
        var path = ReferenceWorkloadPath();
        return new PluginWorkerWorkloadLaunchPolicy(
            Path.GetDirectoryName(path)!,
            path,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            new HashSet<string>(StringComparer.Ordinal));
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

    private sealed class RecordingResource(
        string name,
        List<string> releases,
        bool fail = false) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            releases.Add(name);
            return fail
                ? ValueTask.FromException(new InvalidOperationException(name))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class DeadlineObservingHostBridge
        : IExperimentalPluginWorkerHostBridge
    {
        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IpcEnvelope> HandleRequestAsync(
            IpcEnvelope request,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Deadline cancellation was not observed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
