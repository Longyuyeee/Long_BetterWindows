using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ClipboardMonitoringLeaseManagerTests
{
    [Fact]
    public async Task MultipleConsumers_StartOnceAndStopAfterLastRelease()
    {
        var starts = 0;
        var stops = 0;
        using var leases = new ClipboardMonitoringLeaseManager(
            () =>
            {
                starts++;
                return Task.FromResult(HostApiResponse.Success());
            },
            () =>
            {
                stops++;
                return Task.FromResult(HostApiResponse.Success());
            });

        Assert.True((await leases.AcquireAsync()).IsSuccess);
        Assert.True((await leases.AcquireAsync()).IsSuccess);

        Assert.Equal(2, leases.LeaseCount);
        Assert.True(leases.IsActive);
        Assert.Equal(1, starts);

        Assert.True((await leases.ReleaseAsync()).IsSuccess);
        Assert.Equal(1, leases.LeaseCount);
        Assert.Equal(0, stops);

        Assert.True((await leases.ReleaseAsync()).IsSuccess);
        Assert.False(leases.IsActive);
        Assert.Equal(1, stops);
    }

    [Fact]
    public async Task FailedStart_DoesNotCreateLeaseAndCanRetry()
    {
        var attempts = 0;
        using var leases = new ClipboardMonitoringLeaseManager(
            () =>
            {
                attempts++;
                return Task.FromResult(attempts == 1
                    ? HostApiResponse.Failure(ApiErrorCode.Unknown, "failed")
                    : HostApiResponse.Success());
            },
            () => Task.FromResult(HostApiResponse.Success()));

        Assert.False((await leases.AcquireAsync()).IsSuccess);
        Assert.Equal(0, leases.LeaseCount);

        Assert.True((await leases.AcquireAsync()).IsSuccess);
        Assert.Equal(1, leases.LeaseCount);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task FailedFinalStop_PreservesLeaseForRetry()
    {
        var attempts = 0;
        using var leases = new ClipboardMonitoringLeaseManager(
            () => Task.FromResult(HostApiResponse.Success()),
            () =>
            {
                attempts++;
                return Task.FromResult(attempts == 1
                    ? HostApiResponse.Failure(ApiErrorCode.Unknown, "failed")
                    : HostApiResponse.Success());
            });

        await leases.AcquireAsync();

        Assert.False((await leases.ReleaseAsync()).IsSuccess);
        Assert.Equal(1, leases.LeaseCount);
        Assert.True(leases.IsActive);

        Assert.True((await leases.ReleaseAsync()).IsSuccess);
        Assert.Equal(0, leases.LeaseCount);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task StopAll_StopsOnceAndClearsEveryLease()
    {
        var stops = 0;
        using var leases = new ClipboardMonitoringLeaseManager(
            () => Task.FromResult(HostApiResponse.Success()),
            () =>
            {
                stops++;
                return Task.FromResult(HostApiResponse.Success());
            });

        await leases.AcquireAsync();
        await leases.AcquireAsync();

        Assert.True((await leases.StopAllAsync()).IsSuccess);
        Assert.Equal(0, leases.LeaseCount);
        Assert.Equal(1, stops);
        Assert.True((await leases.StopAllAsync()).IsSuccess);
        Assert.Equal(1, stops);
    }

    [Fact]
    public async Task ConcurrentConsumers_SerializeTheFirstStart()
    {
        var starts = 0;
        var startEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStart = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var leases = new ClipboardMonitoringLeaseManager(
            async () =>
            {
                starts++;
                startEntered.SetResult();
                await allowStart.Task;
                return HostApiResponse.Success();
            },
            () => Task.FromResult(HostApiResponse.Success()));

        var first = leases.AcquireAsync();
        await startEntered.Task;
        var second = leases.AcquireAsync();
        allowStart.SetResult();

        Assert.True((await first).IsSuccess);
        Assert.True((await second).IsSuccess);
        Assert.Equal(1, starts);
        Assert.Equal(2, leases.LeaseCount);
    }

    [Fact]
    public void WebRuntime_UsesLeaseAwareMonitoringAndReleasesDispatcher()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Services", "ClipboardService.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Engine", "WebPluginHostDispatcher.cs"));
        var runtime = File.ReadAllText(Path.Combine(
            root, "src", "LongBetterWindows.Host", "Engine", "WebPluginRuntime.cs"));

        Assert.Contains("_monitorLeases.AcquireAsync()", service);
        Assert.Contains("_monitorLeases.ReleaseAsync()", service);
        Assert.Contains("_clipboardGate.WaitAsync()", dispatcher);
        Assert.Contains("_clipboardAcquireTask", dispatcher);
        Assert.Contains("ReleaseClipboardAfterAcquireAsync", dispatcher);
        Assert.Contains("_hostDispatcher.Dispose()", runtime);
        Assert.DoesNotContain(
            "\"clipboard.startMonitoring\" => Ok(h.Clipboard.StartMonitoringAsync())",
            dispatcher);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
