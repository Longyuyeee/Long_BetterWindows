using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Automation;

internal static class QualityTerminalOutputExportMatrix
{
    private const string OriginalContent = "quality-original";
    private static readonly UTF8Encoding Utf8 = new(false);

    internal static async Task<bool> RunAsync(
        WorkflowTerminalOutput output,
        string matrixDirectory,
        string reportDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(matrixDirectory);
        var writable = Path.Combine(root, "writable");
        var reparse = Path.Combine(root, "reparse");
        var denied = Path.Combine(root, "denied");
        if (!Directory.Exists(writable)
            || !Directory.Exists(reparse)
            || !Directory.Exists(denied))
        {
            throw new DirectoryNotFoundException("Quality export matrix directories are incomplete.");
        }

        var exporter = new WorkflowTerminalOutputExporter();
        var existingTarget = Path.Combine(writable, "existing.txt");
        var missingTarget = Path.Combine(root, "missing", "missing.txt");
        var reparseTarget = Path.Combine(reparse, "reparse.txt");
        var targetChanged = Path.Combine(writable, "target-changed.txt");
        var valueChanged = Path.Combine(writable, "value-changed.txt");
        var deniedTarget = Path.Combine(denied, "permission-denied.txt");
        var successTarget = Path.Combine(writable, "success.txt");

        var existingReview = exporter.Prepare(output, existingTarget);
        var missingReview = exporter.Prepare(output, missingTarget);
        var reparseReview = exporter.Prepare(output, reparseTarget);

        var targetReview = exporter.Prepare(output, targetChanged);
        await File.WriteAllTextAsync(
            targetChanged,
            OriginalContent,
            Utf8,
            cancellationToken);
        var targetResult = await exporter.ExportApprovedAsync(
            output,
            targetChanged,
            targetReview.Fingerprint,
            cancellationToken);

        var valueReview = exporter.Prepare(output, valueChanged);
        var valueResult = await exporter.ExportApprovedAsync(
            output with { Value = output.Value + "-changed" },
            valueChanged,
            valueReview.Fingerprint,
            cancellationToken);

        var deniedReview = exporter.Prepare(output, deniedTarget);
        var deniedResult = await exporter.ExportApprovedAsync(
            output,
            deniedTarget,
            deniedReview.Fingerprint,
            cancellationToken);

        var successReview = exporter.Prepare(output, successTarget);
        var successResult = await exporter.ExportApprovedAsync(
            output,
            successTarget,
            successReview.Fingerprint,
            cancellationToken);
        var successBytes = File.Exists(successTarget)
            ? await File.ReadAllBytesAsync(successTarget, cancellationToken)
            : Array.Empty<byte>();
        var expectedBytes = Utf8.GetBytes(output.Value);
        var successMatches = successBytes.SequenceEqual(expectedBytes)
            && string.Equals(
                successResult.ValueSha256,
                Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant(),
                StringComparison.Ordinal);
        if (File.Exists(successTarget)) File.Delete(successTarget);

        var reportsRedacted = !Directory.Exists(reportDirectory)
            || !Directory.EnumerateFiles(reportDirectory, "*.json", SearchOption.AllDirectories)
                .Any(path => File.ReadAllText(path).Contains(output.Value, StringComparison.Ordinal));
        var temporaryFiles = new[] { writable, Path.Combine(root, "reparse-target") }
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*.tmp",
                SearchOption.AllDirectories))
            .ToArray();

        var evidence = new
        {
            schema_version = 1,
            classification = "workflow_terminal_output_export_matrix",
            value_sha256 = successReview.ValueSha256,
            utf8_byte_count = successReview.Utf8ByteCount,
            cases = new
            {
                existing_target = !existingReview.IsValid && File.ReadAllText(existingTarget) == OriginalContent,
                missing_directory = !missingReview.IsValid && !File.Exists(missingTarget),
                reparse_point = !reparseReview.IsValid && !File.Exists(reparseTarget),
                target_changed = !targetResult.IsSuccess
                    && targetResult.Failure == WorkflowTerminalOutputExportFailure.ReviewInvalid
                    && File.ReadAllText(targetChanged) == OriginalContent,
                value_changed = !valueResult.IsSuccess
                    && valueResult.Failure == WorkflowTerminalOutputExportFailure.ReviewChanged
                    && !File.Exists(valueChanged),
                permission_denied = deniedReview.IsValid
                    && !deniedResult.IsSuccess
                    && deniedResult.Failure == WorkflowTerminalOutputExportFailure.AccessDenied
                    && !File.Exists(deniedTarget),
                success = successResult.IsSuccess && successMatches && !File.Exists(successTarget),
            },
            temporary_files_cleaned = temporaryFiles.Length == 0,
            reports_redacted = reportsRedacted,
        };
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        var passed = !json.Contains(output.Value, StringComparison.Ordinal)
            && evidence.cases.existing_target
            && evidence.cases.missing_directory
            && evidence.cases.reparse_point
            && evidence.cases.target_changed
            && evidence.cases.value_changed
            && evidence.cases.permission_denied
            && evidence.cases.success
            && evidence.temporary_files_cleaned
            && evidence.reports_redacted;

        await File.WriteAllTextAsync(
            Path.Combine(root, "host-export-matrix.json"),
            json,
            Utf8,
            cancellationToken);
        return passed;
    }
}
