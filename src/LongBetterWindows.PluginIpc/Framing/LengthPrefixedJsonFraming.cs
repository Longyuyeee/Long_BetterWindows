using System.Buffers.Binary;
using System.Text.Json;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.PluginIpc.Framing;

public static class LengthPrefixedJsonFraming
{
    public static async ValueTask WriteAsync(
        Stream stream,
        IpcEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(envelope);

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, IpcJson.Options);
        if (payload.Length == 0 || payload.Length > IpcProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"IPC frame must contain 1 to {IpcProtocol.MaximumFrameBytes} bytes.");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IpcEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > IpcProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"IPC frame length {length} is outside the allowed range.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<IpcEnvelope>(payload, IpcJson.Options)
                ?? throw new InvalidDataException("IPC frame contains an empty JSON envelope.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("IPC frame contains invalid JSON.", ex);
        }
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("IPC stream closed before the frame completed.");
            }

            offset += read;
        }
    }
}
