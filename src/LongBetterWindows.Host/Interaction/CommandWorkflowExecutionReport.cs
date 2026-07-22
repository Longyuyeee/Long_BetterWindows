using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record WorkflowExecutionReportDocument(
        int SchemaVersion,
        string ReportId,
        string WorkflowId,
        string WorkflowDefinitionSha256,
        string ExecutionFingerprint,
        WorkflowExecutionStatus Status,
        DateTimeOffset StartedAt,
        DateTimeOffset FinishedAt,
        bool MessagesIncluded,
        string? Message,
        IReadOnlyList<WorkflowExecutionEvent> Events);

    public sealed record WorkflowExecutionReportReadResult(
        bool IsSuccess,
        WorkflowExecutionReportDocument? Report,
        string? Error);

    public sealed record WorkflowExecutionReportSaveOptions(
        bool AllowSensitiveMessages = false);

    public sealed record WorkflowExecutionReportSaveResult(
        bool IsSuccess,
        string? Path,
        string? Error);

    public static class CommandWorkflowExecutionReportCodec
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumEventCount = 256;
        public const int MaximumMessageLength = 4096;

        private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

        public static WorkflowExecutionReportDocument Create(
            CommandWorkflowDefinition workflow,
            CommandWorkflowExecutionResult result,
            bool includeMessages = false,
            string? reportId = null)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            ArgumentNullException.ThrowIfNull(result);
            var now = DateTimeOffset.UtcNow;
            var events = (result.Events ?? Array.Empty<WorkflowExecutionEvent>())
                .Select(item => includeMessages ? item : item with { Message = null })
                .ToList();
            var startedAt = events.Count == 0 ? now : events.Min(item => item.Timestamp);
            var finishedAt = events.Count == 0 ? now : events.Max(item => item.Timestamp);
            var document = new WorkflowExecutionReportDocument(
                CurrentSchemaVersion,
                reportId ?? $"{startedAt.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
                workflow.Id,
                CommandWorkflowDocumentCodec.ComputeDefinitionSha256(workflow),
                result.Fingerprint,
                result.Status,
                startedAt,
                finishedAt,
                includeMessages,
                includeMessages ? result.Message : null,
                events);
            var error = Validate(document);
            if (error is not null) throw new ArgumentException(error, nameof(result));
            return document;
        }

        public static string Serialize(WorkflowExecutionReportDocument report)
        {
            ArgumentNullException.ThrowIfNull(report);
            var error = Validate(report);
            if (error is not null) throw new ArgumentException(error, nameof(report));
            return JsonSerializer.Serialize(report, JsonOptions);
        }

        public static WorkflowExecutionReportReadResult Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Failure("Execution report is empty.");
            try
            {
                var report = JsonSerializer.Deserialize<WorkflowExecutionReportDocument>(json, JsonOptions);
                if (report is null) return Failure("Execution report is empty.");
                var error = Validate(report);
                return error is null
                    ? new WorkflowExecutionReportReadResult(true, report, null)
                    : Failure(error);
            }
            catch (JsonException ex)
            {
                return Failure($"Execution report JSON is invalid: {ex.Message}");
            }
        }

        private static string? Validate(WorkflowExecutionReportDocument report)
        {
            if (report.SchemaVersion != CurrentSchemaVersion)
                return $"Unsupported execution report schema version: {report.SchemaVersion}.";
            if (!IsIdentifier(report.ReportId, 128)) return "Execution report id is invalid.";
            if (!IsIdentifier(report.WorkflowId, 64)) return "Execution report workflow id is invalid.";
            if (!IsHash(report.WorkflowDefinitionSha256)) return "Workflow definition SHA-256 is invalid.";
            if (!IsHash(report.ExecutionFingerprint)) return "Execution fingerprint is invalid.";
            if (!Enum.IsDefined(report.Status)) return "Execution status is invalid.";
            if (report.FinishedAt < report.StartedAt) return "Execution report finish time precedes its start time.";
            if (report.Events is null || report.Events.Count is < 1 or > MaximumEventCount)
                return $"Execution report must contain between 1 and {MaximumEventCount} events.";
            if (!report.MessagesIncluded
                && (report.Message is not null || report.Events.Any(item => item.Message is not null)))
            {
                return "Execution report contains messages without declaring them included.";
            }
            if (report.Message?.Length > MaximumMessageLength)
                return "Execution report message exceeds the maximum length.";
            for (var index = 0; index < report.Events.Count; index++)
            {
                var item = report.Events[index];
                if (item is null) return "Execution report contains a null event.";
                if (item.Sequence != index + 1) return "Execution report event sequence is invalid.";
                if (!Enum.IsDefined(item.Kind)) return "Execution report event kind is invalid.";
                if (item.StepId is not null && !IsIdentifier(item.StepId, 64))
                    return "Execution report event step id is invalid.";
                if (item.Timestamp < report.StartedAt || item.Timestamp > report.FinishedAt)
                    return "Execution report event timestamp is outside the report interval.";
                if (item.Message?.Length > MaximumMessageLength)
                    return "Execution report event message exceeds the maximum length.";
            }
            return null;
        }

        private static bool IsHash(string value)
            => value is not null
                && value.Length == 64
                && value.All(Uri.IsHexDigit);

        private static bool IsIdentifier(string value, int maximumLength)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= maximumLength
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            return options;
        }

        private static WorkflowExecutionReportReadResult Failure(string error)
            => new(false, null, error);
    }

    /// <summary>Creates immutable, atomically-written workflow execution reports.</summary>
    public sealed class CommandWorkflowExecutionReportRepository
    {
        public const long MaximumDocumentBytes = 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly string _root;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public CommandWorkflowExecutionReportRepository(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Execution report root directory must not be empty.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
        }

        public async Task<WorkflowExecutionReportSaveResult> SaveAsync(
            WorkflowExecutionReportDocument report,
            WorkflowExecutionReportSaveOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(report);
            options ??= new WorkflowExecutionReportSaveOptions();
            if (report.MessagesIncluded && !options.AllowSensitiveMessages)
            {
                return SaveFailure(
                    "Execution report includes potentially sensitive messages; explicit persistence approval is required.");
            }

            string json;
            try
            {
                json = CommandWorkflowExecutionReportCodec.Serialize(report);
            }
            catch (ArgumentException ex)
            {
                return SaveFailure(ex.Message);
            }
            var bytes = StrictUtf8.GetBytes(json);
            if (bytes.LongLength > MaximumDocumentBytes)
                return SaveFailure("Execution report exceeds the maximum size.");

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                string targetPath;
                try
                {
                    EnsureRoot();
                    targetPath = GetPath(report.ReportId);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return SaveFailure($"Execution report storage is unavailable: {ex.Message}");
                }
                if (File.Exists(targetPath))
                    return SaveFailure("Execution report already exists and cannot be overwritten.");

                var temporaryPath = Path.Combine(_root, $".{report.ReportId}.{Guid.NewGuid():N}.tmp");
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
                    File.Move(temporaryPath, targetPath);
                    return new WorkflowExecutionReportSaveResult(true, targetPath, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return SaveFailure($"Execution report could not be saved: {ex.Message}");
                }
                finally
                {
                    TryDelete(temporaryPath);
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public Task<WorkflowExecutionReportReadResult> LoadAsync(
            string reportId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reportId))
                return Task.FromResult(ReadFailure("Execution report id is invalid."));
            try
            {
                EnsureRoot();
                return ReadAsync(GetPath(reportId), cancellationToken);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReadFailure($"Execution report storage is unavailable: {ex.Message}"));
            }
        }

        private async Task<WorkflowExecutionReportReadResult> ReadAsync(
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return ReadFailure($"Execution report was not found: {path}");
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    return ReadFailure("Execution report must not be a reparse point.");
                if (file.Length > MaximumDocumentBytes)
                    return ReadFailure("Execution report exceeds the maximum size.");
                byte[] bytes;
                await using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (stream.Length > MaximumDocumentBytes)
                        return ReadFailure("Execution report exceeds the maximum size.");
                    bytes = new byte[checked((int)stream.Length)];
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != bytes.Length) return ReadFailure("Execution report could not be read completely.");
                }
                return CommandWorkflowExecutionReportCodec.Deserialize(StrictUtf8.GetString(bytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return ReadFailure($"Execution report could not be read: {ex.Message}");
            }
        }

        private void EnsureRoot()
        {
            Directory.CreateDirectory(_root);
            if ((new DirectoryInfo(_root).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Execution report root directory must not be a reparse point.");
        }

        private string GetPath(string reportId)
        {
            if (reportId.Length > 128
                || reportId.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not ('.' or '_' or '-')))
            {
                throw new ArgumentException("Execution report id is invalid.", nameof(reportId));
            }
            var path = Path.GetFullPath(Path.Combine(_root, reportId + ".workflow-report.json"));
            var prefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Execution report path escapes the managed root.");
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preserve the report operation result when best-effort cleanup fails.
            }
        }

        private static WorkflowExecutionReportSaveResult SaveFailure(string error)
            => new(false, null, error);

        private static WorkflowExecutionReportReadResult ReadFailure(string error)
            => new(false, null, error);
    }
}
