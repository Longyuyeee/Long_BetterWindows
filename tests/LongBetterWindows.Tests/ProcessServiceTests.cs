using System.Diagnostics;
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
}
