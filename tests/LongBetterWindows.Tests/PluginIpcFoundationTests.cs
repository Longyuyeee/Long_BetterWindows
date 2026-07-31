using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

namespace LongBetterWindows.Tests;

public sealed class PluginIpcFoundationTests
{
    [Fact]
    public void PipeName_IsVersionedStableAndDoesNotExposeSid()
    {
        const string sid = "S-1-5-21-111111111-222222222-333333333-1001";

        var first = BrokerPipeName.ForSid(sid);
        var second = BrokerPipeName.ForSid(sid);

        Assert.Equal(first, second);
        Assert.Matches("^long-plugin-broker-v1-[a-f0-9]{32}$", first);
        Assert.DoesNotContain(sid, first, StringComparison.Ordinal);
        Assert.NotEqual(first, BrokerPipeName.ForSid($"{sid}-different"));
    }

    [Fact]
    public void Protocol_RejectsDeadlinesOutsideContract()
    {
        Assert.Equal(10_000, IpcProtocol.NormalizeDeadline(null));
        Assert.Equal(100, IpcProtocol.NormalizeDeadline(100));
        Assert.Equal(120_000, IpcProtocol.NormalizeDeadline(120_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => IpcProtocol.NormalizeDeadline(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => IpcProtocol.NormalizeDeadline(120_001));
    }

    [Fact]
    public async Task Framing_RoundTripsEnvelopeAndRejectsInvalidLengths()
    {
        var request = IpcEnvelope.Request(
            "health.ping",
            new { nonce = "hello" },
            id: "05bbca5a-1c22-4180-9045-b8c2b7e0740b");
        await using var stream = new MemoryStream();

        await LengthPrefixedJsonFraming.WriteAsync(stream, request);
        stream.Position = 0;
        var restored = await LengthPrefixedJsonFraming.ReadAsync(stream);

        Assert.Equal(IpcProtocol.Name, restored.Protocol);
        Assert.Equal(request.Id, restored.Id);
        Assert.Equal("request", restored.Kind);
        Assert.Equal("health.ping", restored.Method);
        Assert.Equal("hello", restored.Payload!.Value.GetProperty("nonce").GetString());

        foreach (var invalidLength in new[] { 0, -1, IpcProtocol.MaximumFrameBytes + 1 })
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, invalidLength);
            await using var invalid = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(
                async () => await LengthPrefixedJsonFraming.ReadAsync(invalid));
        }
    }

    [Fact]
    public async Task Client_RequiresHelloAndCompletesPingOverRealNamedPipe()
    {
        var pipeName = $"long-ipc-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var client = new LongPluginBrokerClient(pipeName);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RequestAsync<object, object>("health.ping", new { }));

        var serverTask = ServeHelloAndPingAsync(server);
        var hello = await client.ConnectAsync(new HostHelloRequest(
            "Long Grid Test",
            "0.1.0",
            [IpcProtocol.Name],
            ["catalog.read", "command.invoke"]));
        var pong = await client.RequestAsync<object, PingResponse>(
            "health.ping",
            new { nonce = "roundtrip" });

        Assert.True(client.IsConnected);
        Assert.Equal("Long助手", hello.HostName);
        Assert.Equal("roundtrip", pong.Nonce);
        await serverTask;
    }

    [Fact]
    public void ProjectBoundary_DoesNotReferenceHostWpfOrPluginImplementations()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.PluginIpc",
            "LongBetterWindows.PluginIpc.csproj"));

        Assert.DoesNotContain("LongBetterWindows.Host", project);
        Assert.DoesNotContain("UseWPF", project);
        Assert.DoesNotContain("<ProjectReference", project);
    }

    private static async Task ServeHelloAndPingAsync(NamedPipeServerStream server)
    {
        await server.WaitForConnectionAsync();
        var hello = await LengthPrefixedJsonFraming.ReadAsync(server);
        Assert.Equal("host.hello", hello.Method);
        await LengthPrefixedJsonFraming.WriteAsync(server, Response(
            hello.Id,
            new HostHelloResponse(
                "Long助手",
                "1.1.0",
                IpcProtocol.Name,
                ["catalog.read", "command.invoke"],
                IpcProtocol.MaximumFrameBytes,
                IpcProtocol.MaximumDeadlineMilliseconds)));

        var ping = await LengthPrefixedJsonFraming.ReadAsync(server);
        Assert.Equal("health.ping", ping.Method);
        var nonce = ping.Payload!.Value.GetProperty("nonce").GetString();
        await LengthPrefixedJsonFraming.WriteAsync(
            server,
            Response(ping.Id, new PingResponse(nonce!)));
    }

    private static IpcEnvelope Response<T>(string id, T result) => new()
    {
        Id = id,
        Kind = "response",
        Result = JsonSerializer.SerializeToElement(result, IpcJson.Options)
    };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongBetterWindows.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record PingResponse(string Nonce);
}
