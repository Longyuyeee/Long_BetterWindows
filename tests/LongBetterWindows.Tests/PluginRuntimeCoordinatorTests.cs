using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
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

    [Fact]
    public async Task ExecuteRequestedCommandAsync_WritesPrivacyAwareSuccessReport()
    {
        var directory = CreateTemporaryDirectory();
        var reportPath = Path.Combine(directory, "command.json");
        try
        {
            var registry = new PluginRegistry();
            registry.Register(
                new PluginManifest
                {
                    Id = "quality.echo",
                    Name = "Quality echo",
                    Version = "1.0.0",
                    EntryPoint = "quality.echo.dll",
                    Commands =
                    [
                        new PluginCommand
                        {
                            Id = "echo",
                            Title = "Echo",
                            AcceptedInputs = [AcceptedInputType.Text],
                        },
                    ],
                },
                new EchoCommandPlugin(),
                null,
                directory);

            var exitCode = await PluginRuntimeCoordinator.ExecuteRequestedCommandAsync(
                registry,
                new PluginRuntimeStartRequest(
                    "quality.echo:echo",
                    "private input",
                    true,
                    reportPath));

            Assert.Equal(0, exitCode);
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(reportPath));
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
            Assert.Equal("quality.echo:echo", root.GetProperty("command_key").GetString());
            Assert.Equal("quality.echo", root.GetProperty("plugin_id").GetString());
            Assert.Equal("echo", root.GetProperty("command_id").GetString());
            Assert.Equal("text", root.GetProperty("input_type").GetString());
            Assert.Equal(13, root.GetProperty("input_text_length").GetInt32());
            Assert.Equal(64, root.GetProperty("input_text_sha256").GetString()!.Length);
            Assert.DoesNotContain("private input", await File.ReadAllTextAsync(reportPath));
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(
                "echoed",
                root.GetProperty("outputs").GetProperty("result")
                    .GetProperty("value").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CapabilityUsageSnapshot_IsStableWhileLiveCountersContinue()
    {
        const string pluginId = "quality.snapshot";
        var tracker = CapabilityUsageTracker.Instance;
        tracker.ClearStats(pluginId);
        try
        {
            tracker.RecordApiCall(pluginId, "system.read", "system.read.first");
            var snapshot = tracker.GetStatsSnapshot(pluginId);
            tracker.RecordApiCall(pluginId, "system.read", "system.read.second");

            Assert.NotNull(snapshot);
            Assert.Equal(1, snapshot.TotalCalls);
            Assert.Single(snapshot.ApiMethodCalls);
            Assert.Equal(2, tracker.GetStats(pluginId)!.TotalCalls);
        }
        finally
        {
            tracker.ClearStats(pluginId);
        }
    }

    [Fact]
    public async Task QualityCommandFixture_ProvidesStatefulSafeCapabilities()
    {
        var directory = CreateTemporaryDirectory();
        var fixturePath = Path.Combine(directory, "fixture.json");
        try
        {
            await File.WriteAllTextAsync(
                fixturePath,
                """
                {
                  "schema_version": 1,
                  "clipboard": { "text": "fixture clipboard" },
                  "storage": { "values": { "existing": "before" } },
                  "http": {
                    "data": "[[[\"Long Assistant\"]],null,\"zh-CN\"]",
                    "required_url_contains": "translate"
                  }
                }
                """);
            using var scope = QualityCommandFixture.Install(fixturePath);
            var fixture = QualityCommandFixture.Current!;

            Assert.True(fixture.TryDispatch(
                "clipboard.getText",
                [],
                out var clipboardResponse));
            Assert.Contains(
                "fixture clipboard",
                JsonSerializer.Serialize(await clipboardResponse));
            Assert.True(fixture.TryDispatch(
                "storage.compareExchange",
                ["existing", "before", "after"],
                out var storageResponse));
            Assert.Contains(
                "\"data\":true",
                JsonSerializer.Serialize(await storageResponse));
            Assert.True(fixture.TryDispatch(
                "http.get",
                ["https://example.test/translate"],
                out var httpResponse));
            Assert.Contains(
                "Long Assistant",
                JsonSerializer.Serialize(await httpResponse));

            var snapshot = fixture.CreateSnapshot();
            Assert.Equal("fixture clipboard", snapshot.ClipboardText);
            Assert.Equal("after", snapshot.Storage["existing"]);
            Assert.Equal(1, snapshot.Calls["clipboard.getText"]);
            Assert.Equal(1, snapshot.Calls["storage.compareExchange"]);
            Assert.Equal(1, snapshot.Calls["http.get"]);
            Assert.Matches("^[a-f0-9]{64}$", snapshot.LastHttpUrlSha256!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    private sealed class EchoCommandPlugin : ILongPlugin, IPluginCommandHandler
    {
        public string Id => "quality.echo";
        public string Name => "Quality echo";
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
            => Task.FromResult(PluginCommandResult.Success(
                outputs: new Dictionary<string, PluginCommandOutput>
                {
                    ["result"] = new(PluginCommandOutputType.Text, "echoed"),
                }));
    }
}
