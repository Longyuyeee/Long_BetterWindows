using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WidgetPlacement(
        string InstanceId,
        string PluginId,
        string WidgetId,
        int Column,
        int Row,
        int Columns,
        int Rows);

    internal sealed record WidgetLayoutSnapshot(
        long Revision,
        IReadOnlyList<WidgetPlacement> Placements)
    {
        internal static WidgetLayoutSnapshot Empty { get; } = new(0, []);
    }

    internal sealed record WidgetLayoutLoadResult(
        bool IsSuccess,
        WidgetLayoutSnapshot Snapshot,
        string? Error);

    internal sealed record WidgetLayoutSaveResult(
        bool IsSuccess,
        string? Error);

    internal sealed class WidgetLayoutStore
    {
        internal const int SchemaVersion = 1;
        internal const int MaximumPlacements = 256;
        internal const int MaximumDocumentBytes = 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _root;
        private readonly string _path;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        internal WidgetLayoutStore(string? rootDirectory = null)
        {
            _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LongBetterWindows",
                "Widgets"));
            _path = Path.Combine(_root, "layout.json");
        }

        internal string LayoutPath => _path;

        internal async Task<WidgetLayoutLoadResult> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_path))
                    return new WidgetLayoutLoadResult(true, WidgetLayoutSnapshot.Empty, null);
                var file = new FileInfo(_path);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    return Failure("Widget layout document must not be a reparse point.");
                if (file.Length > MaximumDocumentBytes)
                    return Failure("Widget layout document exceeds 1 MiB.");

                byte[] bytes;
                await using (var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    bytes = new byte[checked((int)stream.Length)];
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var read = await stream.ReadAsync(
                            bytes.AsMemory(offset),
                            cancellationToken);
                        if (read == 0)
                            break;
                        offset += read;
                    }
                    if (offset != bytes.Length)
                        return Failure("Widget layout document could not be read completely.");
                }

                var document = JsonSerializer.Deserialize<WidgetLayoutDocument>(
                    StrictUtf8.GetString(bytes),
                    JsonOptions);
                if (document is null || document.SchemaVersion != SchemaVersion)
                    return Failure("Widget layout schema version is unsupported.");
                if (document.Revision < 0
                    || document.Placements.Count > MaximumPlacements
                    || document.Placements.Any(placement => !IsStoredPlacementValid(placement)))
                {
                    return Failure("Widget layout document contains invalid placement data.");
                }

                return new WidgetLayoutLoadResult(
                    true,
                    new WidgetLayoutSnapshot(
                        document.Revision,
                        document.Placements
                            .Select(placement => placement.ToPlacement())
                            .ToArray()),
                    null);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException
                    or JsonException)
            {
                return Failure($"Widget layout could not be read: {exception.Message}");
            }
        }

        internal async Task<WidgetLayoutSaveResult> SaveAsync(
            WidgetLayoutSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.Revision < 0
                || snapshot.Placements.Count > MaximumPlacements
                || snapshot.Placements.Any(placement => !IsPlacementValid(placement)))
            {
                return new WidgetLayoutSaveResult(false, "Widget layout is invalid.");
            }

            var document = new WidgetLayoutDocument
            {
                SchemaVersion = SchemaVersion,
                Revision = snapshot.Revision,
                Placements = snapshot.Placements
                    .Select(StoredWidgetPlacement.FromPlacement)
                    .ToList(),
            };
            var bytes = StrictUtf8.GetBytes(JsonSerializer.Serialize(document, JsonOptions));
            if (bytes.Length > MaximumDocumentBytes)
                return new WidgetLayoutSaveResult(false, "Widget layout exceeds 1 MiB.");

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                EnsureRoot();
                if (File.Exists(_path)
                    && (File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0)
                {
                    return new WidgetLayoutSaveResult(
                        false,
                        "Widget layout document must not be a reparse point.");
                }
                var temporaryPath = Path.Combine(
                    _root,
                    $".layout.{Guid.NewGuid():N}.tmp");
                try
                {
                    await using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await stream.WriteAsync(bytes, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporaryPath, _path, overwrite: true);
                    return new WidgetLayoutSaveResult(true, null);
                }
                finally
                {
                    TryDelete(temporaryPath);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return new WidgetLayoutSaveResult(
                    false,
                    $"Widget layout could not be saved: {exception.Message}");
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private static bool IsStoredPlacementValid(StoredWidgetPlacement placement)
            => IsIdentity(placement.InstanceId)
                && IsIdentity(placement.PluginId)
                && IsIdentity(placement.WidgetId)
                && IsGridPlacementValid(
                    placement.Column,
                    placement.Row,
                    placement.Columns,
                    placement.Rows);

        internal static bool IsPlacementValid(WidgetPlacement placement)
            => IsIdentity(placement.InstanceId)
                && IsIdentity(placement.PluginId)
                && IsIdentity(placement.WidgetId)
                && IsGridPlacementValid(
                    placement.Column,
                    placement.Row,
                    placement.Columns,
                    placement.Rows);

        internal static bool Overlaps(WidgetPlacement left, WidgetPlacement right)
            => left.Column < right.Column + right.Columns
                && left.Column + left.Columns > right.Column
                && left.Row < right.Row + right.Rows
                && left.Row + left.Rows > right.Row;

        private static bool IsGridPlacementValid(
            int column,
            int row,
            int columns,
            int rows)
            => column >= 0
                && row >= 0
                && columns is >= 1 and <= 24
                && rows is >= 1 and <= 24
                && column + columns <= 24
                && row <= 100_000;

        private static bool IsIdentity(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 128
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

        private static WidgetLayoutLoadResult Failure(string error)
            => new(false, WidgetLayoutSnapshot.Empty, error);

        private void EnsureRoot()
        {
            Directory.CreateDirectory(_root);
            if ((new DirectoryInfo(_root).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Widget layout root must not be a reparse point.");
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private sealed class WidgetLayoutDocument
        {
            [JsonPropertyName("schema_version")]
            public int SchemaVersion { get; init; }

            [JsonPropertyName("revision")]
            public long Revision { get; init; }

            [JsonPropertyName("placements")]
            public List<StoredWidgetPlacement> Placements { get; init; } = [];
        }

        private sealed class StoredWidgetPlacement
        {
            [JsonPropertyName("instance_id")]
            public string InstanceId { get; init; } = string.Empty;

            [JsonPropertyName("plugin_id")]
            public string PluginId { get; init; } = string.Empty;

            [JsonPropertyName("widget_id")]
            public string WidgetId { get; init; } = string.Empty;

            [JsonPropertyName("column")]
            public int Column { get; init; }

            [JsonPropertyName("row")]
            public int Row { get; init; }

            [JsonPropertyName("columns")]
            public int Columns { get; init; }

            [JsonPropertyName("rows")]
            public int Rows { get; init; }

            internal WidgetPlacement ToPlacement()
                => new(InstanceId, PluginId, WidgetId, Column, Row, Columns, Rows);

            internal static StoredWidgetPlacement FromPlacement(WidgetPlacement placement)
                => new()
                {
                    InstanceId = placement.InstanceId,
                    PluginId = placement.PluginId,
                    WidgetId = placement.WidgetId,
                    Column = placement.Column,
                    Row = placement.Row,
                    Columns = placement.Columns,
                    Rows = placement.Rows,
                };
        }
    }
}
