using System.Text.Json;
using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

var options = ProbeOptions.Parse(args);
var json = new JsonSerializerOptions(IpcJson.Options) { WriteIndented = true };

try
{
    await using var client = new LongPluginBrokerClient(options.PipeName);
    var hello = await client.ConnectAsync(new HostHelloRequest(
        "Long IPC Probe",
        "1.0.0",
        [IpcProtocol.Name],
        [
            BrokerMethods.HealthPing,
            BrokerMethods.PluginCatalogList,
            BrokerMethods.PluginCatalogGet,
            BrokerMethods.CommandInvoke,
            BrokerMethods.CommandCancel,
            BrokerMethods.PluginOpen,
        ]), options.ConnectTimeoutMilliseconds);

    object result = options.Operation switch
    {
        "hello" => hello,
        "ping" => await client.RequestAsync<HealthPingRequest, HealthPingResponse>(
            BrokerMethods.HealthPing, new HealthPingRequest(options.Nonce)),
        "get" => await client.RequestAsync<PluginCatalogGetRequest, PluginCatalogGetResponse>(
            BrokerMethods.PluginCatalogGet, new PluginCatalogGetRequest(options.PluginId!)),
        "open" => await client.RequestAsync<PluginOpenRequest, PluginOpenResponse>(
            BrokerMethods.PluginOpen, new PluginOpenRequest(options.PluginId!)),
        "invoke" => await client.RequestAsync<CommandInvokeRequest, CommandInvokeResponse>(
            BrokerMethods.CommandInvoke,
            new CommandInvokeRequest(
                options.PluginId!,
                options.CommandId!,
                options.Arguments,
                options.InputType,
                options.Text),
            options.DeadlineMilliseconds),
        _ => await client.RequestAsync<PluginCatalogListRequest, PluginCatalogListResponse>(
            BrokerMethods.PluginCatalogList, new PluginCatalogListRequest()),
    };

    Console.WriteLine(JsonSerializer.Serialize(result, json));
    return 0;
}
catch (IpcRemoteException ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        error = ex.Code,
        message = ex.Message,
        retryable = ex.Retryable,
    }, json));
    return 2;
}
catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        error = IpcErrorCodes.HostUnavailable,
        message = ex.Message,
        retryable = true,
    }, json));
    return 3;
}

internal sealed record ProbeOptions(
    string Operation,
    string? PipeName,
    string? PluginId,
    string? CommandId,
    string InputType,
    string? Text,
    string? Nonce,
    int ConnectTimeoutMilliseconds,
    int DeadlineMilliseconds,
    IReadOnlyDictionary<string, string> Arguments)
{
    public static ProbeOptions Parse(string[] args)
    {
        var values = args.ToList();
        string? Read(string option)
        {
            var index = values.IndexOf(option);
            if (index < 0 || index + 1 >= values.Count)
                return null;
            return values[index + 1];
        }

        var operation = values.Contains("--hello") ? "hello"
            : values.Contains("--ping") ? "ping"
            : values.Contains("--get") ? "get"
            : values.Contains("--open") ? "open"
            : values.Contains("--invoke") ? "invoke"
            : "catalog";
        var marker = "--" + operation;
        var operationIndex = values.IndexOf(marker);
        var pluginId = operation is "get" or "open" or "invoke"
            && operationIndex + 1 < values.Count ? values[operationIndex + 1] : null;
        var commandId = operation == "invoke" && operationIndex + 2 < values.Count
            ? values[operationIndex + 2] : null;
        if ((operation is "get" or "open") && string.IsNullOrWhiteSpace(pluginId)
            || operation == "invoke" && (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(commandId)))
            throw new ArgumentException("Usage: --get <plugin-id>, --open <plugin-id>, or --invoke <plugin-id> <command-id>.");

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count - 1; index++)
        {
            if (values[index] != "--arg") continue;
            var pair = values[index + 1].Split('=', 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                throw new ArgumentException("--arg must use key=value syntax.");
            arguments[pair[0]] = pair[1];
        }

        return new ProbeOptions(
            operation,
            Read("--pipe"),
            pluginId,
            commandId,
            Read("--input") ?? "none",
            Read("--text"),
            Read("--nonce") ?? Guid.NewGuid().ToString("N"),
            ParseInteger(Read("--connect-timeout"), 5_000),
            IpcProtocol.NormalizeDeadline(ParseInteger(Read("--deadline"), 10_000)),
            arguments);
    }

    private static int ParseInteger(string? value, int fallback)
        => value is null ? fallback
            : int.TryParse(value, out var parsed) ? parsed
            : throw new ArgumentException($"Expected an integer but received '{value}'.");
}
