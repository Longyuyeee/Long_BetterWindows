using System.IO;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;
using LongBetterWindows.PluginIpc.Framing;

namespace LongBetterWindows.Host.Broker;

internal sealed class BrokerConnection(
    Stream stream,
    PluginCatalogProjection catalog,
    string hostVersion)
{
    internal static readonly string[] Features =
    [
        BrokerMethods.HealthPing,
        BrokerMethods.PluginCatalogList,
        BrokerMethods.PluginCatalogGet,
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var helloCompleted = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            IpcEnvelope request;
            try
            {
                request = await LengthPrefixedJsonFraming.ReadAsync(stream, cancellationToken);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException)
            {
                return;
            }

            if (request.Protocol != IpcProtocol.Name)
            {
                await WriteErrorAsync(
                    request.Id,
                    IpcErrorCodes.IncompatibleProtocol,
                    "Unsupported IPC protocol.",
                    cancellationToken);
                return;
            }

            if (!IsValidRequest(request, out var validationError))
            {
                await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, validationError, cancellationToken);
                continue;
            }

            if (!helloCompleted && request.Method != BrokerMethods.HostHello)
            {
                await WriteErrorAsync(
                    request.Id,
                    IpcErrorCodes.Unauthenticated,
                    "host.hello must be the first request.",
                    cancellationToken);
                return;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(IpcProtocol.NormalizeDeadline(request.DeadlineMilliseconds));
            try
            {
                if (request.Method == BrokerMethods.HostHello)
                {
                    if (helloCompleted)
                    {
                        await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "host.hello was already completed.", deadline.Token);
                        continue;
                    }

                    var hello = Deserialize<HostHelloRequest>(request);
                    if (!hello.Protocols.Contains(IpcProtocol.Name, StringComparer.Ordinal))
                    {
                        await WriteErrorAsync(request.Id, IpcErrorCodes.IncompatibleProtocol, "No compatible IPC protocol was offered.", deadline.Token);
                        return;
                    }

                    await WriteResultAsync(request.Id, new HostHelloResponse(
                        "Long助手",
                        hostVersion,
                        IpcProtocol.Name,
                        Features,
                        IpcProtocol.MaximumFrameBytes,
                        IpcProtocol.MaximumDeadlineMilliseconds), deadline.Token);
                    helloCompleted = true;
                    continue;
                }

                switch (request.Method)
                {
                    case BrokerMethods.HealthPing:
                        var ping = Deserialize<HealthPingRequest>(request);
                        await WriteResultAsync(request.Id, new HealthPingResponse(ping.Nonce, DateTimeOffset.UtcNow), deadline.Token);
                        break;
                    case BrokerMethods.PluginCatalogList:
                        await WriteResultAsync(request.Id, catalog.List(Deserialize<PluginCatalogListRequest>(request)), deadline.Token);
                        break;
                    case BrokerMethods.PluginCatalogGet:
                        var result = catalog.Get(Deserialize<PluginCatalogGetRequest>(request));
                        if (result is null)
                            await WriteErrorAsync(request.Id, IpcErrorCodes.PluginNotFound, "The requested plugin is not installed.", deadline.Token);
                        else
                            await WriteResultAsync(request.Id, result, deadline.Token);
                        break;
                    default:
                        await WriteErrorAsync(request.Id, IpcErrorCodes.SurfaceNotSupported, "The requested broker surface is not available.", deadline.Token);
                        break;
                }
            }
            catch (JsonException)
            {
                await WriteErrorAsync(request.Id, IpcErrorCodes.InvalidRequest, "The request payload has an invalid shape.", cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await WriteErrorAsync(request.Id, IpcErrorCodes.Timeout, "The request deadline elapsed.", cancellationToken);
            }
        }
    }

    private static bool IsValidRequest(IpcEnvelope request, out string error)
    {
        if (request.Kind != "request" || !Guid.TryParse(request.Id, out _) || string.IsNullOrWhiteSpace(request.Method))
        {
            error = "The request envelope is invalid.";
            return false;
        }
        try { _ = IpcProtocol.NormalizeDeadline(request.DeadlineMilliseconds); }
        catch (ArgumentOutOfRangeException)
        {
            error = "The request deadline is outside the allowed range.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static T Deserialize<T>(IpcEnvelope envelope)
    {
        if (envelope.Payload is not JsonElement payload)
            throw new JsonException("Request payload is missing.");
        return payload.Deserialize<T>(IpcJson.Options)
               ?? throw new JsonException("Request payload is empty.");
    }

    private ValueTask WriteResultAsync<T>(string id, T result, CancellationToken cancellationToken)
        => LengthPrefixedJsonFraming.WriteAsync(stream, new IpcEnvelope
        {
            Id = id,
            Kind = "response",
            Result = JsonSerializer.SerializeToElement(result, IpcJson.Options),
        }, cancellationToken);

    private ValueTask WriteErrorAsync(string id, string code, string message, CancellationToken cancellationToken)
        => LengthPrefixedJsonFraming.WriteAsync(stream, new IpcEnvelope
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
            Kind = "response",
            Error = new IpcError(code, message),
        }, cancellationToken);
}
