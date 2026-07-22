using System.IO;
using System.Text;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record CommandWorkflowSaveOptions(
        bool AllowSensitiveInputs = false,
        string? ExpectedExistingDefinitionSha256 = null);

    public sealed record CommandWorkflowSaveResult(
        bool IsSuccess,
        string? Path,
        string DefinitionSha256,
        string? Error);

    public sealed record ManagedCommandWorkflowSummary(
        string Id,
        string Name,
        WorkflowFailureMode FailureMode,
        int StepCount,
        string DefinitionSha256,
        DateTimeOffset UpdatedAt,
        bool ContainsSensitiveInputs);

    public sealed record CommandWorkflowListIssue(
        string FileName,
        string Error);

    public sealed record CommandWorkflowListResult(
        bool IsSuccess,
        IReadOnlyList<ManagedCommandWorkflowSummary> Workflows,
        IReadOnlyList<CommandWorkflowListIssue> Issues,
        string? Error);

    public sealed record CommandWorkflowDeleteResult(
        bool IsSuccess,
        string? Error);

    public sealed record CommandWorkflowExportResult(
        bool IsSuccess,
        string? Path,
        string DefinitionSha256,
        string? Error);

    /// <summary>Atomically stores local workflows and reads external imports without adopting them.</summary>
    public sealed class CommandWorkflowRepository
    {
        public const long MaximumDocumentBytes = 4 * 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly string _root;
        private readonly string _localSourceId;
        private readonly WorkflowSourceTrustPolicy _trustPolicy;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public CommandWorkflowRepository(
            string rootDirectory,
            string localSourceId,
            WorkflowSourceTrustPolicy? trustPolicy = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Workflow root directory must not be empty.", nameof(rootDirectory));
            if (!IsSourceId(localSourceId))
                throw new ArgumentException("Local workflow source id is invalid.", nameof(localSourceId));
            _root = Path.GetFullPath(rootDirectory);
            _localSourceId = localSourceId;
            _trustPolicy = trustPolicy ?? WorkflowSourceTrustPolicy.Empty;
        }

        public async Task<CommandWorkflowSaveResult> SaveAsync(
            CommandWorkflowDefinition workflow,
            CommandWorkflowSaveOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            options ??= new CommandWorkflowSaveOptions();
            if (CommandWorkflowDocumentCodec.ContainsSensitiveInputs(workflow)
                && !options.AllowSensitiveInputs)
            {
                return SaveFailure(
                    "Workflow contains text, paths, image data, or arguments; explicit sensitive-input persistence approval is required.");
            }

            string json;
            WorkflowDocumentReadResult validation;
            try
            {
                json = CommandWorkflowDocumentCodec.Serialize(
                    workflow,
                    new WorkflowDocumentSource(WorkflowDocumentSourceKind.LocalManaged, _localSourceId));
                validation = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: true, _trustPolicy);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return SaveFailure(ex.Message);
            }
            if (!validation.IsSuccess)
                return SaveFailure(validation.Error ?? "Workflow document validation failed.");

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                string targetPath;
                try
                {
                    EnsureManagedRoot();
                    targetPath = GetManagedPath(validation.Workflow!.Id);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return SaveFailure($"Workflow storage is unavailable: {ex.Message}");
                }

                if (File.Exists(targetPath))
                {
                    if (string.IsNullOrWhiteSpace(options.ExpectedExistingDefinitionSha256))
                        return SaveFailure("Updating a workflow requires its existing definition SHA-256.");
                    if (!TryNormalizeHash(options.ExpectedExistingDefinitionSha256, out var expectedHash))
                        return SaveFailure("Expected workflow hash must be 64 hexadecimal characters.");
                    var existing = await ReadAsync(targetPath, isManagedFile: true, cancellationToken);
                    if (!existing.IsSuccess)
                        return SaveFailure(existing.Error ?? "Existing workflow could not be read.");
                    if (!string.Equals(existing.DefinitionSha256, expectedHash, StringComparison.Ordinal))
                        return SaveFailure("Workflow changed since it was loaded; refusing a stale update.");
                }
                else if (!string.IsNullOrWhiteSpace(options.ExpectedExistingDefinitionSha256))
                {
                    return SaveFailure("Expected workflow version does not exist.");
                }

                var bytes = StrictUtf8.GetBytes(json);
                if (bytes.LongLength > MaximumDocumentBytes)
                    return SaveFailure("Workflow document exceeds the maximum size.");
                var temporaryPath = Path.Combine(
                    _root,
                    $".{validation.Workflow.Id}.{Guid.NewGuid():N}.tmp");
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
                    File.Move(temporaryPath, targetPath, overwrite: true);
                    return new CommandWorkflowSaveResult(
                        true,
                        targetPath,
                        validation.DefinitionSha256,
                        null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return SaveFailure($"Workflow document could not be saved: {ex.Message}");
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

        public Task<WorkflowDocumentReadResult> LoadManagedAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            if (!IsIdentifier(workflowId))
                return Task.FromResult(ReadFailure("Workflow id is invalid."));
            try
            {
                EnsureManagedRoot();
                return ReadAsync(GetManagedPath(workflowId), isManagedFile: true, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReadFailure($"Workflow storage is unavailable: {ex.Message}"));
            }
        }

        public async Task<CommandWorkflowListResult> ListManagedAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureManagedRoot();
                var workflows = new List<ManagedCommandWorkflowSummary>();
                var issues = new List<CommandWorkflowListIssue>();
                foreach (var path in Directory.EnumerateFiles(
                    _root,
                    "*.workflow.json",
                    SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await ReadAsync(path, isManagedFile: true, cancellationToken);
                    if (!result.IsSuccess)
                    {
                        issues.Add(new CommandWorkflowListIssue(
                            Path.GetFileName(path),
                            result.Error ?? "Workflow document could not be read."));
                        continue;
                    }
                    var workflow = result.Workflow!;
                    workflows.Add(new ManagedCommandWorkflowSummary(
                        workflow.Id,
                        workflow.Name,
                        workflow.FailureMode,
                        workflow.Steps.Count,
                        result.DefinitionSha256,
                        new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                        CommandWorkflowDocumentCodec.ContainsSensitiveInputs(workflow)));
                }
                return new CommandWorkflowListResult(
                    true,
                    workflows
                        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    issues,
                    null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new CommandWorkflowListResult(
                    false,
                    Array.Empty<ManagedCommandWorkflowSummary>(),
                    Array.Empty<CommandWorkflowListIssue>(),
                    $"Workflow storage is unavailable: {ex.Message}");
            }
        }

        public async Task<CommandWorkflowDeleteResult> DeleteManagedAsync(
            string workflowId,
            string expectedDefinitionSha256,
            CancellationToken cancellationToken = default)
        {
            if (!IsIdentifier(workflowId))
                return new CommandWorkflowDeleteResult(false, "Workflow id is invalid.");
            if (!TryNormalizeHash(expectedDefinitionSha256, out var expectedHash))
                return new CommandWorkflowDeleteResult(false, "Expected workflow hash must be 64 hexadecimal characters.");

            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                string path;
                try
                {
                    EnsureManagedRoot();
                    path = GetManagedPath(workflowId);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new CommandWorkflowDeleteResult(false, $"Workflow storage is unavailable: {ex.Message}");
                }
                var existing = await ReadAsync(path, isManagedFile: true, cancellationToken);
                if (!existing.IsSuccess)
                    return new CommandWorkflowDeleteResult(false, existing.Error);
                if (!string.Equals(existing.DefinitionSha256, expectedHash, StringComparison.Ordinal))
                {
                    return new CommandWorkflowDeleteResult(
                        false,
                        "Workflow changed since it was loaded; refusing a stale delete.");
                }
                try
                {
                    File.Delete(path);
                    return new CommandWorkflowDeleteResult(true, null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new CommandWorkflowDeleteResult(false, $"Workflow could not be deleted: {ex.Message}");
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public Task<WorkflowDocumentReadResult> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return Task.FromResult(ReadFailure("Workflow import path must not be empty."));
            try
            {
                return ReadAsync(Path.GetFullPath(sourcePath), isManagedFile: false, cancellationToken);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(ReadFailure($"Workflow import path is invalid: {ex.Message}"));
            }
        }

        public async Task<CommandWorkflowExportResult> ExportManagedAsync(
            string workflowId,
            string expectedDefinitionSha256,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (!IsIdentifier(workflowId)) return ExportFailure("Workflow id is invalid.");
            if (!TryNormalizeHash(expectedDefinitionSha256, out var expectedHash))
                return ExportFailure("Expected workflow hash must be 64 hexadecimal characters.");
            if (string.IsNullOrWhiteSpace(destinationPath))
                return ExportFailure("Workflow export path must not be empty.");

            string targetPath;
            string targetDirectory;
            try
            {
                targetPath = Path.GetFullPath(destinationPath);
                targetDirectory = Path.GetDirectoryName(targetPath)
                    ?? throw new IOException("Workflow export directory is invalid.");
                if (IsWithinManagedRoot(targetPath))
                    return ExportFailure("Workflow exports must be written outside the managed workflow directory.");
                var directory = new DirectoryInfo(targetDirectory);
                if (!directory.Exists) return ExportFailure("Workflow export directory does not exist.");
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return ExportFailure("Workflow export directory must not be a reparse point.");
                if (File.Exists(targetPath)
                    && (File.GetAttributes(targetPath) & FileAttributes.ReparsePoint) != 0)
                {
                    return ExportFailure("Workflow export target must not be a reparse point.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return ExportFailure($"Workflow export path is invalid: {ex.Message}");
            }

            var managed = await LoadManagedAsync(workflowId, cancellationToken);
            if (!managed.IsSuccess)
                return ExportFailure(managed.Error ?? "Managed workflow could not be read.");
            if (!string.Equals(managed.DefinitionSha256, expectedHash, StringComparison.Ordinal))
                return ExportFailure("Workflow changed since it was loaded; refusing a stale export.");

            byte[] bytes;
            try
            {
                var json = CommandWorkflowDocumentCodec.Serialize(
                    managed.Workflow!,
                    new WorkflowDocumentSource(
                        WorkflowDocumentSourceKind.Imported,
                        $"{_localSourceId}:export"));
                bytes = StrictUtf8.GetBytes(json);
                if (bytes.LongLength > MaximumDocumentBytes)
                    return ExportFailure("Workflow document exceeds the maximum size.");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return ExportFailure(ex.Message);
            }

            var temporaryPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
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
                File.Move(temporaryPath, targetPath, overwrite: true);
                return new CommandWorkflowExportResult(
                    true,
                    targetPath,
                    managed.DefinitionSha256,
                    null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ExportFailure($"Workflow document could not be exported: {ex.Message}");
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private async Task<WorkflowDocumentReadResult> ReadAsync(
            string path,
            bool isManagedFile,
            CancellationToken cancellationToken)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return ReadFailure($"Workflow document was not found: {path}");
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    return ReadFailure("Workflow document must not be a reparse point.");
                if (file.Length > MaximumDocumentBytes)
                    return ReadFailure("Workflow document exceeds the maximum size.");

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
                        return ReadFailure("Workflow document exceeds the maximum size.");
                    bytes = new byte[checked((int)stream.Length)];
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
                        if (read == 0) break;
                        offset += read;
                    }
                    if (offset != bytes.Length) return ReadFailure("Workflow document could not be read completely.");
                }
                var json = StrictUtf8.GetString(bytes);
                return CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile, _trustPolicy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return ReadFailure($"Workflow document could not be read: {ex.Message}");
            }
        }

        private void EnsureManagedRoot()
        {
            Directory.CreateDirectory(_root);
            var root = new DirectoryInfo(_root);
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Workflow root directory must not be a reparse point.");
        }

        private string GetManagedPath(string workflowId)
        {
            var path = Path.GetFullPath(Path.Combine(_root, workflowId + ".workflow.json"));
            var prefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Workflow path escapes the managed root.");
            return path;
        }

        private bool IsWithinManagedRoot(string path)
        {
            var root = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryNormalizeHash(string value, out string normalized)
        {
            normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            {
                normalized = string.Empty;
                return false;
            }
            return true;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The write result is more useful than a best-effort temporary-file cleanup error.
            }
        }

        private static bool IsIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 64
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

        private static bool IsSourceId(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 128
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-' or ':');

        private static CommandWorkflowSaveResult SaveFailure(string error)
            => new(false, null, string.Empty, error);

        private static CommandWorkflowExportResult ExportFailure(string error)
            => new(false, null, string.Empty, error);

        private static WorkflowDocumentReadResult ReadFailure(string error)
            => new(false, null, null, WorkflowDocumentTrustLevel.Untrusted, string.Empty, null, error);
    }
}
