using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Tests;

public sealed class PluginIpcGoldenFixtureTests
{
    [Fact]
    public void Golden_envelopes_round_trip_without_semantic_drift()
    {
        foreach (var path in Directory.GetFiles(FixtureDirectory(), "*.json"))
        {
            var source = File.ReadAllText(path);
            var envelope = JsonSerializer.Deserialize<IpcEnvelope>(source, IpcJson.Options);
            Assert.NotNull(envelope);
            Assert.Equal(IpcProtocol.Name, envelope.Protocol);
            Assert.True(Guid.TryParse(envelope.Id, out _));

            var expected = JsonNode.Parse(source);
            var actual = JsonNode.Parse(JsonSerializer.Serialize(envelope, IpcJson.Options));
            Assert.True(JsonNode.DeepEquals(expected, actual), Path.GetFileName(path));
        }
    }

    [Fact]
    public void Golden_requests_match_public_contract_types()
    {
        var hello = Read("host-hello.request.json");
        Assert.Equal(BrokerMethods.HostHello, hello.Method);
        Assert.Equal("Long Grid Fixture", hello.Payload!.Value
            .Deserialize<HostHelloRequest>(IpcJson.Options)?.ClientName);

        var command = Read("command-invoke.request.json");
        Assert.Equal(BrokerMethods.CommandInvoke, command.Method);
        var invocation = command.Payload!.Value.Deserialize<CommandInvokeRequest>(IpcJson.Options);
        Assert.Equal("com.long.hardware-monitor", invocation?.PluginId);
        Assert.Equal("refresh", invocation?.CommandId);

        var open = Read("plugin-open.request.json");
        Assert.Equal(BrokerMethods.PluginOpen, open.Method);
        Assert.Equal("com.long.quick-note", open.Payload!.Value
            .Deserialize<PluginOpenRequest>(IpcJson.Options)?.PluginId);
    }

    [Fact]
    public void Golden_responses_match_public_contract_types_and_errors()
    {
        var hello = Read("host-hello.response.json");
        var negotiation = hello.Result!.Value.Deserialize<HostHelloResponse>(IpcJson.Options);
        Assert.Contains(BrokerMethods.PluginOpen, negotiation?.Features ?? []);

        var command = Read("command-invoke.response.json");
        var result = command.Result!.Value.Deserialize<CommandInvokeResponse>(IpcJson.Options);
        Assert.Equal("completed", result?.Status);
        Assert.Equal("CPU 12%", result?.Outputs["summary"].Value);

        var error = Read("error.response.json");
        Assert.Equal(IpcErrorCodes.PluginNotFound, error.Error?.Code);
        Assert.False(error.Error?.Retryable);
    }

    private static IpcEnvelope Read(string name)
        => JsonSerializer.Deserialize<IpcEnvelope>(
               File.ReadAllText(Path.Combine(FixtureDirectory(), name)),
               IpcJson.Options)
           ?? throw new InvalidDataException($"IPC fixture is empty: {name}");

    private static string FixtureDirectory()
        => Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "ipc");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongBetterWindows.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
