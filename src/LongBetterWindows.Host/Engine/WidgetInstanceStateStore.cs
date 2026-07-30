using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WidgetInstanceStateStore
    {
        private readonly string _rootDirectory;
        private readonly SemaphoreSlim _gate = new(1, 1);

        internal WidgetInstanceStateStore(string? rootDirectory = null)
        {
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LongBetterWindows",
                    "WidgetState")
                : rootDirectory;
        }

        internal async Task<JsonElement?> GetAsync(
            WebPluginBridgeContext context,
            CancellationToken cancellationToken = default)
        {
            if (!TryBuildStatePath(context, out var path))
                return null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path))
                    return null;

                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return document.RootElement.GetProperty("state").Clone();
            }
            catch (JsonException)
            {
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task SetAsync(
            WebPluginBridgeContext context,
            object? state,
            CancellationToken cancellationToken = default)
        {
            if (!TryBuildStatePath(context, out var path))
                throw new InvalidOperationException("Widget instance identity is incomplete.");

            var envelope = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema_version = 1,
                plugin_id = context.PluginId,
                widget_id = context.WidgetId,
                instance_id = context.InstanceId,
                updated_at_utc = DateTimeOffset.UtcNow,
                state,
            });

            if (envelope.Length > WebPluginBridgeContext.InstanceStateLimitBytes + 4096)
                throw new InvalidOperationException("Widget instance state envelope is too large.");

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tempPath, envelope, cancellationToken).ConfigureAwait(false);

                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal bool TryBuildStatePath(WebPluginBridgeContext context, out string path)
        {
            path = string.Empty;
            if (!context.IsWidget
                || string.IsNullOrWhiteSpace(context.WidgetId)
                || string.IsNullOrWhiteSpace(context.InstanceId))
            {
                return false;
            }

            path = Path.Combine(
                _rootDirectory,
                SafeSegment(context.PluginId),
                SafeSegment(context.WidgetId),
                HashIdentity(context.PluginId, context.WidgetId!, context.InstanceId!) + ".json");
            return true;
        }

        private static string SafeSegment(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '_');
            }

            return builder.Length == 0 ? "_" : builder.ToString();
        }

        private static string HashIdentity(string pluginId, string widgetId, string instanceId)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
                pluginId + "\n" + widgetId + "\n" + instanceId));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
