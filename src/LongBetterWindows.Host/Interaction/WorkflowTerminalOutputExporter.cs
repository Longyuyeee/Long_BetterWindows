using System.IO;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record WorkflowTerminalOutputExportReview(
        bool IsValid,
        string Fingerprint,
        string? DestinationPath,
        string ValueSha256,
        int Utf8ByteCount,
        IReadOnlyList<string> Issues);

    public sealed record WorkflowTerminalOutputExportResult(
        bool IsSuccess,
        string? Path,
        string? ValueSha256,
        string? Error);

    /// <summary>Reviews and atomically creates a plaintext export without overwriting an existing path.</summary>
    public sealed class WorkflowTerminalOutputExporter
    {
        private const int MaximumValueCharacters = 65_536;
        private const int MaximumValueBytes = MaximumValueCharacters * 4;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public WorkflowTerminalOutputExportReview Prepare(
            WorkflowTerminalOutput output,
            string destinationPath)
        {
            ArgumentNullException.ThrowIfNull(output);
            var issues = ValidateOutput(output);
            string? targetPath = null;
            if (!TryValidateDestination(destinationPath, issues, out targetPath))
                targetPath = null;

            byte[] valueBytes;
            try
            {
                valueBytes = StrictUtf8.GetBytes(output.Value);
                if (valueBytes.Length > MaximumValueBytes)
                    issues.Add("Terminal output exceeds the maximum export size.");
            }
            catch (EncoderFallbackException)
            {
                valueBytes = Array.Empty<byte>();
                issues.Add("Terminal output is not valid Unicode text.");
            }

            var valueSha256 = Convert.ToHexString(SHA256.HashData(valueBytes)).ToLowerInvariant();
            var fingerprint = issues.Count == 0 && targetPath is not null
                ? ComputeFingerprint(output, targetPath, valueSha256, valueBytes.Length)
                : string.Empty;
            return new WorkflowTerminalOutputExportReview(
                issues.Count == 0,
                fingerprint,
                targetPath,
                valueSha256,
                valueBytes.Length,
                issues);
        }

        public async Task<WorkflowTerminalOutputExportResult> ExportApprovedAsync(
            WorkflowTerminalOutput output,
            string destinationPath,
            string reviewedFingerprint,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (string.IsNullOrWhiteSpace(reviewedFingerprint))
                return Failure("Terminal output export approval is missing.");

            var review = Prepare(output, destinationPath);
            if (!review.IsValid)
                return Failure(string.Join(" ", review.Issues));
            if (!string.Equals(review.Fingerprint, reviewedFingerprint, StringComparison.Ordinal))
                return Failure("Terminal output or export destination changed after review.");

            var targetPath = review.DestinationPath!;
            var targetDirectory = Path.GetDirectoryName(targetPath)!;
            var temporaryPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = StrictUtf8.GetBytes(output.Value);
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

                var finalIssues = new List<string>();
                if (!TryValidateDestination(targetPath, finalIssues, out var finalPath)
                    || !string.Equals(targetPath, finalPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(string.Join(" ", finalIssues.DefaultIfEmpty(
                        "Terminal output export destination changed during export.")));
                }

                File.Move(temporaryPath, targetPath);
                return new WorkflowTerminalOutputExportResult(
                    true,
                    targetPath,
                    review.ValueSha256,
                    null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure("Terminal output export was cancelled.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure($"Terminal output could not be exported: {exception.Message}");
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static List<string> ValidateOutput(WorkflowTerminalOutput output)
        {
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(output.StepId))
                issues.Add("Terminal output step id must not be empty.");
            if (string.IsNullOrWhiteSpace(output.OutputKey))
                issues.Add("Terminal output key must not be empty.");
            if (output.Type is not PluginCommandOutputType.Text and not PluginCommandOutputType.Path)
                issues.Add("Terminal output type is not supported for export.");
            if (output.Value.Length > MaximumValueCharacters)
                issues.Add("Terminal output exceeds the maximum character count.");
            if (output.Type == PluginCommandOutputType.Path && string.IsNullOrWhiteSpace(output.Value))
                issues.Add("Terminal Path output must not be empty.");
            return issues;
        }

        private static bool TryValidateDestination(
            string destinationPath,
            ICollection<string> issues,
            out string? targetPath)
        {
            targetPath = null;
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                issues.Add("Terminal output export path must not be empty.");
                return false;
            }
            try
            {
                targetPath = Path.GetFullPath(destinationPath);
                var fileName = Path.GetFileName(targetPath);
                if (string.IsNullOrEmpty(fileName))
                    issues.Add("Terminal output export target must be a file.");
                else if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    issues.Add("Terminal output export file name contains invalid characters.");
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                    issues.Add("Terminal output export target already exists and cannot be overwritten.");

                var directoryPath = Path.GetDirectoryName(targetPath);
                if (directoryPath is null || !Directory.Exists(directoryPath))
                {
                    issues.Add("Terminal output export directory does not exist.");
                    return false;
                }
                for (var directory = new DirectoryInfo(directoryPath);
                     directory is not null;
                     directory = directory.Parent)
                {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add("Terminal output export path must not contain reparse-point directories.");
                        break;
                    }
                }
                return issues.Count == 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                issues.Add($"Terminal output export path is invalid: {exception.Message}");
                targetPath = null;
                return false;
            }
        }

        private static string ComputeFingerprint(
            WorkflowTerminalOutput output,
            string targetPath,
            string valueSha256,
            int utf8ByteCount)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
            {
                writer.Write(output.StepId);
                writer.Write(output.OutputKey);
                writer.Write((int)output.Type);
                writer.Write(targetPath.ToUpperInvariant());
                writer.Write(valueSha256);
                writer.Write(utf8ByteCount);
            }
            return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        }

        private static WorkflowTerminalOutputExportResult Failure(string error)
            => new(false, null, null, error);

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Best effort cleanup must not hide the original export result.
            }
        }
    }
}
