using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using LongBetterWindows.Host.Broker;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
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

    [Fact]
    public async Task Broker_invokes_existing_command_executor_with_normalized_input()
    {
        var handler = new RecordingCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());

        var response = await client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
            BrokerMethods.CommandInvoke,
            new CommandInvokeRequest(
                "command.plugin", "run",
                new Dictionary<string, string> { ["count"] = "3" },
                "text", "hello"));

        Assert.Equal("completed", response.Status);
        Assert.Equal("done", response.Message);
        Assert.Equal("hello", handler.LastInvocation?.Text);
        Assert.Equal("3", handler.LastInvocation?.Arguments["count"]);
    }

    [Fact]
    public async Task Broker_cancels_active_command_on_same_connection()
    {
        var handler = new BlockingCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        var requestId = Guid.NewGuid().ToString();
        var invocation = client.RequestWithIdAsync<CommandInvokeRequest, CommandInvokeResponse>(
            requestId,
            BrokerMethods.CommandInvoke,
            CommandRequest(),
            10_000);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancel = await client.CancelCommandAsync(requestId);
        Assert.True(cancel.Accepted);
        var error = await Assert.ThrowsAsync<IpcRemoteException>(() => invocation);
        Assert.Equal(IpcErrorCodes.Cancelled, error.Code);
        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Broker_enforces_command_deadline()
    {
        var handler = new BlockingCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        var error = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
                BrokerMethods.CommandInvoke, CommandRequest(), 100));
        Assert.Equal(IpcErrorCodes.Timeout, error.Code);
    }

    [Fact]
    public async Task Broker_limits_each_plugin_to_four_concurrent_commands()
    {
        var handler = new ConcurrentCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        var requests = Enumerable.Range(0, 5).Select(_ =>
            client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
                BrokerMethods.CommandInvoke, CommandRequest(), 10_000)).ToArray();
        await handler.FourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, handler.MaximumConcurrent);
        handler.Release.TrySetResult();
        await Task.WhenAll(requests);
        Assert.Equal(4, handler.MaximumConcurrent);
    }

    [Fact]
    public async Task Broker_cancels_commands_when_client_disconnects()
    {
        var handler = new BlockingCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        _ = client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
            BrokerMethods.CommandInvoke, CommandRequest(), 10_000);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DisposeAsync();
        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Client_local_wait_cancellation_does_not_poison_connection()
    {
        var handler = new DelayedCommandHandler();
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(handler));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        using var localCancellation = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
                BrokerMethods.CommandInvoke,
                CommandRequest(),
                1_000,
                localCancellation.Token));
        await handler.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        var ping = await client.RequestAsync<HealthPingRequest, HealthPingResponse>(
            BrokerMethods.HealthPing, new HealthPingRequest("still-connected"));
        Assert.Equal("still-connected", ping.Nonce);
    }

    [Fact]
    public async Task Broker_maps_missing_plugin_and_command_errors()
    {
        await using var fixture = await BrokerFixture.StartAsync(CreateCommandRegistry(new RecordingCommandHandler()));
        await using var client = new LongPluginBrokerClient(fixture.PipeName);
        await client.ConnectAsync(Hello());
        var missingPlugin = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
                BrokerMethods.CommandInvoke,
                CommandRequest() with { PluginId = "missing.plugin" }));
        Assert.Equal(IpcErrorCodes.PluginNotFound, missingPlugin.Code);
        var missingCommand = await Assert.ThrowsAsync<IpcRemoteException>(() =>
            client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
                BrokerMethods.CommandInvoke,
                CommandRequest() with { CommandId = "missing" }));
        Assert.Equal(IpcErrorCodes.CommandNotFound, missingCommand.Code);
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

    private static CommandInvokeRequest CommandRequest() => new(
        "command.plugin", "run", new Dictionary<string, string>());

    private static PluginRegistry CreateCommandRegistry(IPluginCommandHandler handler)
    {
        var registry = new PluginRegistry();
        registry.Register(new PluginManifest
        {
            Id = "command.plugin",
            Name = "Command plugin",
            Version = "1.0.0",
            Commands = [new PluginCommand
            {
                Id = "run",
                Title = "Run",
                AcceptedInputs = [AcceptedInputType.None, AcceptedInputType.Text],
                ArgumentSchema = [new PluginCommandArgumentDeclaration
                {
                    Key = "count",
                    Type = PluginCommandArgumentType.Integer,
                    Required = false,
                }],
            }],
        }, handler, null, @"C:\test\command-plugin");
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

    private sealed class RecordingCommandHandler : IPluginCommandHandler
    {
        public PluginCommandInvocation? LastInvocation { get; private set; }
        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            LastInvocation = invocation;
            return Task.FromResult(PluginCommandResult.Success("done"));
        }
    }

    private sealed class BlockingCommandHandler : IPluginCommandHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return PluginCommandResult.Success();
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class ConcurrentCommandHandler : IPluginCommandHandler
    {
        private int _current;
        private int _maximum;
        public int MaximumConcurrent => Volatile.Read(ref _maximum);
        public TaskCompletionSource FourStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _current);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maximum);
            } while (current > observed && Interlocked.CompareExchange(ref _maximum, current, observed) != observed);
            if (current >= 4) FourStarted.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return PluginCommandResult.Success();
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private sealed class DelayedCommandHandler : IPluginCommandHandler
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(100, cancellationToken);
            Completed.TrySetResult();
            return PluginCommandResult.Success();
        }
    }
}
