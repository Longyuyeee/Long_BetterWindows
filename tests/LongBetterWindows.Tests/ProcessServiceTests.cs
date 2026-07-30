using System.Diagnostics;
using System.Globalization;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ProcessServiceTests
{
    [Fact]
    public async Task KillVerifiedAsync_RejectsChangedProcessIdentity()
    {
        using var current = Process.GetCurrentProcess();
        var service = new ProcessService();

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
        var service = new ProcessService();

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
            var service = new ProcessService();

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
}
