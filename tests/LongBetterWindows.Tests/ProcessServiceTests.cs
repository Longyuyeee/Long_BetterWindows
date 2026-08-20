using System.Diagnostics;
using System.Globalization;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

[Collection(PerformanceSensitiveCollection.Name)]
public sealed class ProcessServiceTests
{
    [Fact]
    public async Task KillVerifiedAsync_RejectsChangedProcessIdentity()
    {
        using var current = Process.GetCurrentProcess();
        var service = new ProcessService(new NetworkPortService());

        var result = await service.KillVerifiedAsync(
            current.Id,
            current.ProcessName,
            "stale-process-identity");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
        Assert.False(current.HasExited);
    }

    [Fact]
    public async Task KillVerifiedAsync_RequiresCompleteIdentity()
    {
        using var current = Process.GetCurrentProcess();
        var service = new ProcessService(new NetworkPortService());

        var result = await service.KillVerifiedAsync(
            current.Id,
            current.ProcessName,
            string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
        Assert.False(current.HasExited);
    }

    [Fact]
    public async Task KillVerifiedAsync_TerminatesMatchingDisposableProcess()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Assert.NotNull(process);

        try
        {
            var identity = process.StartTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture);
            var service = new ProcessService(new NetworkPortService());

            var result = await service.KillVerifiedAsync(
                process.Id,
                process.ProcessName,
                identity);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task KillPortOwnerVerifiedAsync_RejectsChangedEndpointOwnership()
    {
        var networkPorts = new NetworkPortService();
        using var listener = await StartDisposableTcpListenerAsync();
        var process = listener.Process;

        try
        {
            var snapshot = await WaitForTcpListenerAsync(networkPorts, listener);
            snapshot.State = "ESTABLISHED";
            var service = new ProcessService(networkPorts);

            var result = await service.KillPortOwnerVerifiedAsync(snapshot);

            Assert.False(result.IsSuccess);
            Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
            Assert.False(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task KillPortOwnerVerifiedAsync_RejectsCurrentHostProcess()
    {
        using var current = Process.GetCurrentProcess();
        var service = new ProcessService(new NetworkPortService());
        var snapshot = new PortInfo
        {
            ProcessId = current.Id,
            ProcessName = current.ProcessName,
            ProcessIdentity = current.StartTime
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            LocalPort = 1,
            LocalAddress = "127.0.0.1",
            Protocol = "TCP",
            State = "LISTENING",
        };

        var result = await service.KillPortOwnerVerifiedAsync(snapshot);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.InvalidArgument, result.ErrorCode);
        Assert.False(current.HasExited);
    }

    [Fact]
    public async Task KillPortOwnerVerifiedAsync_TerminatesMatchingDisposablePortOwner()
    {
        var networkPorts = new NetworkPortService();
        using var listener = await StartDisposableTcpListenerAsync();
        var process = listener.Process;

        try
        {
            var snapshot = await WaitForTcpListenerAsync(networkPorts, listener);
            var service = new ProcessService(networkPorts);

            var result = await service.KillPortOwnerVerifiedAsync(snapshot);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(process.HasExited || process.WaitForExit(5_000));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<DisposableTcpListener> StartDisposableTcpListenerAsync()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command \"$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0); $listener.Start(); [Console]::Out.WriteLine($listener.LocalEndpoint.Port); [Console]::Out.Flush(); Start-Sleep -Seconds 30\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert.NotNull(process);

        try
        {
            var portText = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(int.TryParse(portText, out var port), $"Unexpected listener port: {portText}");
            return new DisposableTcpListener(process, port);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    private static async Task<PortInfo> WaitForTcpListenerAsync(
        NetworkPortService networkPorts,
        DisposableTcpListener listenerProcess)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var result = await networkPorts.GetTcpListenersAsync();
            Assert.True(result.IsSuccess, result.ErrorMessage);
            var listener = result.Data?.FirstOrDefault(item =>
                item.LocalPort == listenerProcess.Port &&
                item.ProcessId == listenerProcess.Process.Id);
            if (listener != null && !string.IsNullOrEmpty(listener.ProcessIdentity))
                return listener;

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException(
            $"TCP listener {listenerProcess.Port} for PID {listenerProcess.Process.Id} was not observed.");
    }

    private sealed class DisposableTcpListener(Process process, int port) : IDisposable
    {
        public Process Process { get; } = process;
        public int Port { get; } = port;

        public void Dispose()
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
            Process.Dispose();
        }
    }
}
