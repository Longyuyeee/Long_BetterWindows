using System.IO;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class SparsePackageServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _scriptPath;

    public SparsePackageServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _scriptPath = Path.Combine(_directory, "manage-sparse-package.ps1");
        File.WriteAllText(_scriptPath, "# test");
    }

    [Fact]
    public async Task GetStatusAsync_ParsesTheLastJsonStateAfterPowerShellNoise()
    {
        var runner = new FakeRunner(new SparsePackageProcessResult(
            0,
            "progress\r\n{\"succeeded\":true,\"message\":\"ok\",\"installed\":true," +
            "\"identity_name\":\"Long.LongBetterWindows\",\"version\":\"1.9.0.0\"," +
            "\"architecture\":\"X64\",\"status\":\"Ok\"}\r\n",
            string.Empty));
        var service = new SparsePackageService(_scriptPath, runner);

        var result = await service.GetStatusAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.State);
        Assert.True(result.State.Installed);
        Assert.Equal("1.9.0.0", result.State.Version);
        Assert.Equal(new[] { "-Action", "Status" }, runner.Arguments);
    }

    [Fact]
    public async Task RegisterOrUpgradeAsync_PassesPathsAsSeparateProcessArguments()
    {
        var runner = new FakeRunner(new SparsePackageProcessResult(
            0,
            "{\"succeeded\":true,\"message\":\"registered\",\"installed\":true}",
            string.Empty));
        var service = new SparsePackageService(_scriptPath, runner);
        var candidate = Path.Combine(_directory, "candidate package.msix");

        var result = await service.RegisterOrUpgradeAsync(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal("-Action", runner.Arguments[0]);
        Assert.Equal("Register", runner.Arguments[1]);
        Assert.Equal("-PackagePath", runner.Arguments[2]);
        Assert.Equal(Path.GetFullPath(candidate), runner.Arguments[3]);
        Assert.Equal("-ExternalLocation", runner.Arguments[4]);
        Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory), runner.Arguments[5]);
    }

    [Fact]
    public async Task FailedScript_PreservesReturnedStateAndFailureMessage()
    {
        var runner = new FakeRunner(new SparsePackageProcessResult(
            1,
            "{\"succeeded\":false,\"message\":\"signature rejected\"," +
            "\"installed\":true,\"version\":\"1.8.0.0\"}",
            "native error"));
        var service = new SparsePackageService(_scriptPath, runner);

        var result = await service.UnregisterAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("signature rejected", result.Message);
        Assert.True(result.State?.Installed);
        Assert.Equal("1.8.0.0", result.State?.Version);
    }

    [Fact]
    public async Task MissingPublishedScript_IsReportedWithoutStartingPowerShell()
    {
        var runner = new FakeRunner(new SparsePackageProcessResult(
            0,
            string.Empty,
            string.Empty));
        var service = new SparsePackageService(
            Path.Combine(_directory, "missing.ps1"),
            runner);

        var result = await service.GetStatusAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("未随应用发布", result.Message);
        Assert.Empty(runner.Arguments);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeRunner(SparsePackageProcessResult result)
        : ISparsePackageProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<SparsePackageProcessResult> RunAsync(
            string scriptPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Assert.True(File.Exists(scriptPath));
            Arguments = arguments.ToArray();
            return Task.FromResult(result);
        }
    }
}
