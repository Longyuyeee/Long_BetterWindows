using System.IO;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class PluginRuntimeCoordinatorTests
{
    [Fact]
    public async Task StartAsync_InitializesAnEmptyPluginDirectory()
    {
        var pluginsDirectory = CreateTemporaryDirectory();
        try
        {
            using var coordinator = new PluginRuntimeCoordinator(
                pluginsDirectory,
                new PluginRegistry());

            var result = await coordinator.StartAsync(
                new PluginRuntimeStartRequest(null, null, false));

            Assert.Equal(0, result.LoadedPluginCount);
            Assert.Equal(0, result.RecoveredTransactionCount);
            Assert.Equal(0, result.InstalledPackageCount);
            Assert.Null(result.ExitCode);
            Assert.NotNull(coordinator.PackageInstaller);
        }
        finally
        {
            Directory.Delete(pluginsDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteRequestedCommandAsync_MissingCommandReturnsExplicitExitCode()
    {
        var registry = new PluginRegistry();

        var exitCode = await PluginRuntimeCoordinator.ExecuteRequestedCommandAsync(
            registry,
            new PluginRuntimeStartRequest("missing.command", null, true));
        var interactiveResult = await PluginRuntimeCoordinator.ExecuteRequestedCommandAsync(
            registry,
            new PluginRuntimeStartRequest("missing.command", null, false));

        Assert.Equal(2, exitCode);
        Assert.Null(interactiveResult);
    }

    [Fact]
    public async Task ExecuteRequestedCommandAsync_WithoutCommandDoesNothing()
    {
        var exitCode = await PluginRuntimeCoordinator.ExecuteRequestedCommandAsync(
            new PluginRegistry(),
            new PluginRuntimeStartRequest(null, "unused", true));

        Assert.Null(exitCode);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "LongBetterWindows.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
