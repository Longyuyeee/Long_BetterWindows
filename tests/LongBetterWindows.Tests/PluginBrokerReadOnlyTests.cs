using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.Host.Broker;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

namespace LongBetterWindows.Tests;

public sealed class PluginBrokerReadOnlyTests
{
    [Fact]
    public void Authentication_requires_same_sid_session_and_integrity()
    {
        var server = new BrokerClientIdentity("S-1-5-21-1", 7, 0x2000);
        Assert.True(BrokerClientAuthentication.IsSameSecurityBoundary(
            server, new BrokerClientIdentity("s-1-5-21-1", 7, 0x2000)));
        Assert.False(BrokerClientAuthentication.IsSameSecurityBoundary(
            server, new BrokerClientIdentity("S-1-5-21-2", 7, 0x2000)));
        Assert.False(BrokerClientAuthentication.IsSameSecurityBoundary(
            server, new BrokerClientIdentity("S-1-5-21-1", 8, 0x2000)));
        Assert.False(BrokerClientAuthentication.IsSameSecurityBoundary(
            server, new BrokerClientIdentity("S-1-5-21-1", 7, 0x3000)));
    }

    [Fact]
    public async Task Broker_negotiates_and_returns_sanitized_catalog()
    {
        var registry = CreateRegistry();
        await using var fixture = await BrokerFixture.StartAsync(registry);
        await using var client = new LongPluginBrokerClient(fixture.PipeName);

        var hello = await client.ConnectAsync(Hello());
        Assert.Equal(IpcProtocol.Name, hello.Protocol);
        Assert.Equal(BrokerConnection.Features, hello.Features);

        var ping = await client.RequestAsync<HealthPingRequest, HealthPingResponse>(
            BrokerMethods.HealthPing, new HealthPingRequest("round-trip"));
        Assert.Equal("round-trip", ping.Nonce);

        var catalog = await client.RequestAsync<PluginCatalogListRequest, PluginCatalogListResponse>(
            BrokerMethods.PluginCatalogList, new PluginCatalogListRequest());
        var plugin = Assert.Single(catalog.Plugins);
        Assert.Equal("test.plugin", plugin.Id);
        Assert.Equal("Test plugin", plugin.Name);
        Assert.Single(plugin.Commands);
        Assert.Single(plugin.Widgets);

        var wireShape = JsonSerializer.Serialize(plugin, IpcJson.Options);
        Assert.DoesNotContain("entry_point", wireShape, StringComparison.Ordinal);
        Assert.DoesNotContain("capabilities", wireShape, StringComparison.Ordinal);
        Assert.DoesNotContain("default_settings", wireShape, StringComparison.Ordinal);
        Assert.DoesNotContain("directory", wireShape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_supports_revision_and_not_found_contracts()
    {
        var registry = CreateRegistry();
        await using var fixture = await BrokerFixture.StartAsync(registry);
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());

        var unchanged = await client.RequestAsync<PluginCatalogListRequest, PluginCatalogListResponse>(
            BrokerMethods.PluginCatalogList,
            new PluginCatalogListRequest(registry.CatalogRevision));
        Assert.True(unchanged.NotModified);
        Assert.Empty(unchanged.Plugins);

        var error = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.RequestAsync<PluginCatalogGetRequest, PluginCatalogGetResponse>(
                BrokerMethods.PluginCatalogGet,
                new PluginCatalogGetRequest("missing.plugin")));
        Assert.Equal(IpcErrorCodes.PluginNotFound, error.Code);
    }

    [Fact]
    public async Task Broker_rejects_requests_before_hello()
    {
        await using var fixture = await BrokerFixture.StartAsync(CreateRegistry());
        await using var pipe = await ConnectRawAsync(fixture.PipeName);
        var request = IpcEnvelope.Request(BrokerMethods.HealthPing, new HealthPingRequest());
        await LengthPrefixedJsonFraming.WriteAsync(pipe, request);
        var response = await LengthPrefixedJsonFraming.ReadAsync(pipe);
        Assert.Equal(IpcErrorCodes.Unauthenticated, response.Error?.Code);
    }

    [Fact]
    public async Task Broker_rejects_incompatible_protocol()
    {
        await using var fixture = await BrokerFixture.StartAsync(CreateRegistry());
        await using var pipe = await ConnectRawAsync(fixture.PipeName);
        var request = IpcEnvelope.Request(BrokerMethods.HostHello, Hello()) with
        {
            Protocol = "unknown/9.0",
        };
        await LengthPrefixedJsonFraming.WriteAsync(pipe, request);
        var response = await LengthPrefixedJsonFraming.ReadAsync(pipe);
        Assert.Equal(IpcErrorCodes.IncompatibleProtocol, response.Error?.Code);
    }

    [Fact]
    public async Task Broker_handles_multiple_clients()
    {
        await using var fixture = await BrokerFixture.StartAsync(CreateRegistry());
        await using var first = new LongPluginBrokerClient(fixture.PipeName);
        await using var second = new LongPluginBrokerClient(fixture.PipeName);
        await Task.WhenAll(first.ConnectAsync(Hello()), second.ConnectAsync(Hello()));
        var responses = await Task.WhenAll(
            first.RequestAsync<HealthPingRequest, HealthPingResponse>(BrokerMethods.HealthPing, new("a")),
            second.RequestAsync<HealthPingRequest, HealthPingResponse>(BrokerMethods.HealthPing, new("b")));
        Assert.Equal(["a", "b"], responses.Select(response => response.Nonce!).Order().ToArray());
    }

    [Fact]
    public async Task Broker_closes_oversized_frames_without_allocating_payload()
    {
        await using var fixture = await BrokerFixture.StartAsync(CreateRegistry());
        await using var pipe = await ConnectRawAsync(fixture.PipeName);
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, IpcProtocol.MaximumFrameBytes + 1);
        await pipe.WriteAsync(prefix);
        await pipe.FlushAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1];
        Assert.Equal(0, await pipe.ReadAsync(buffer, timeout.Token));
    }

    [Fact]
    public async Task Broker_rejects_client_outside_security_boundary()
    {
        var server = new BrokerClientIdentity("S-1-5-21-server", 1, 0x2000);
        var client = new BrokerClientIdentity("S-1-5-21-client", 1, 0x2000);
        await using var fixture = await BrokerFixture.StartAsync(CreateRegistry(), server, client);
        await using var pipe = await ConnectRawAsync(fixture.PipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1];
        Assert.Equal(0, await pipe.ReadAsync(buffer, timeout.Token));
    }

    private static HostHelloRequest Hello() => new(
        "broker-tests", "1.0.0", [IpcProtocol.Name], []);

    private static PluginRegistry CreateRegistry()
    {
        var registry = new PluginRegistry();
        registry.Register(new PluginManifest
        {
            Id = "test.plugin",
            Name = "Test plugin",
            Description = "Public description",
            Author = "Long",
            Version = "1.2.3",
            Runtime = "webview",
            EntryPoint = "secret/path/index.html",
            Capabilities = ["filesystem.read"],
            DefaultSettings = new Dictionary<string, object> { ["token"] = "secret" },
            Commands = [new PluginCommand { Id = "open", Title = "Open", AcceptedInputs = [AcceptedInputType.Text] }],
            Widgets = [new PluginWidgetDefinition { Id = "summary", Title = "Summary", EntryPoint = "secret/widget.html" }],
        }, new object(), null, @"C:\secret\plugin");
        return registry;
    }

    private static async Task<NamedPipeClientStream> ConnectRawAsync(string pipeName)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5_000);
        return pipe;
    }

    private sealed class BrokerFixture : IAsyncDisposable
    {
        private readonly LongPluginBrokerService _service;
        public string PipeName { get; }

        private BrokerFixture(LongPluginBrokerService service, string pipeName)
        {
            _service = service;
            PipeName = pipeName;
        }

        public static Task<BrokerFixture> StartAsync(
            PluginRegistry registry,
            BrokerClientIdentity? serverIdentity = null,
            BrokerClientIdentity? clientIdentity = null)
        {
            var pipeName = $"long-broker-tests-{Guid.NewGuid():N}";
            serverIdentity ??= new BrokerClientIdentity("S-1-5-21-test", 1, 0x2000);
            clientIdentity ??= serverIdentity;
            var service = new LongPluginBrokerService(
                registry, pipeName, new FixedIdentityProbe(serverIdentity, clientIdentity));
            service.Start();
            return Task.FromResult(new BrokerFixture(service, pipeName));
        }

        public ValueTask DisposeAsync() => _service.DisposeAsync();
    }

    private sealed class FixedIdentityProbe(
        BrokerClientIdentity server,
        BrokerClientIdentity client) : IBrokerClientIdentityProbe
    {
        public BrokerClientIdentity GetServerIdentity() => server;
        public BrokerClientIdentity GetClientIdentity(NamedPipeServerStream pipe) => client;
    }
}
