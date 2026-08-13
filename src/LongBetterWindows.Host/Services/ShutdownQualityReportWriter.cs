using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Services;

internal static class ShutdownQualityReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static void WriteNew(
        string outputPath,
        string sourceCommit,
        string hostExecutable,
        int processId,
        int exitCode,
        IReadOnlyList<ShutdownStepResult> results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostExecutable);
        ArgumentNullException.ThrowIfNull(results);

        var fullPath = Path.GetFullPath(outputPath);
        var executablePath = Path.GetFullPath(hostExecutable);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var report = new ShutdownQualityReport(
            1,
            DateTimeOffset.UtcNow,
            "host_shutdown",
            sourceCommit.Trim(),
            executablePath,
            CalculateSha256(executablePath),
            processId,
            exitCode,
            results.All(result => result.Status == ShutdownStepStatus.Passed),
            results.Select(ToReportStep).ToArray());

        using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        JsonSerializer.Serialize(stream, report, JsonOptions);
        stream.WriteByte((byte)'\n');
    }

    private static ShutdownQualityReportStep ToReportStep(
        ShutdownStepResult result)
    {
        var lateStatus = "not_applicable";
        var lateErrorCategory = "none";
        double? lateElapsedMilliseconds = null;
        if (result.LateCompletion is { } lateCompletion)
        {
            if (lateCompletion.IsCompletedSuccessfully)
            {
                var completion = lateCompletion.Result;
                lateStatus = ToValue(completion.Status);
                lateErrorCategory = ToValue(completion.ErrorCategory);
                lateElapsedMilliseconds = completion.ElapsedMilliseconds;
            }
            else
            {
                lateStatus = "pending";
            }
        }

        return new ShutdownQualityReportStep(
            result.Name,
            ToValue(result.Status),
            Math.Round(result.ElapsedMilliseconds, 1),
            result.TimeoutMilliseconds is { } timeout
                ? Math.Round(timeout, 1)
                : null,
            ToValue(result.ErrorCategory),
            lateStatus,
            lateElapsedMilliseconds is { } lateElapsed
                ? Math.Round(lateElapsed, 1)
                : null);
    }

    private static string CalculateSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ToValue(ShutdownStepStatus status)
        => status switch
        {
            ShutdownStepStatus.Passed => "passed",
            ShutdownStepStatus.Failed => "failed",
            ShutdownStepStatus.TimedOut => "timed_out",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static string ToValue(ShutdownErrorCategory category)
        => category switch
        {
            ShutdownErrorCategory.None => "none",
            ShutdownErrorCategory.OperationFailed => "operation_failed",
            ShutdownErrorCategory.InvalidTimeout => "invalid_timeout",
            ShutdownErrorCategory.Timeout => "timeout",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private sealed record ShutdownQualityReport(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
        [property: JsonPropertyName("classification")] string Classification,
        [property: JsonPropertyName("source_commit")] string SourceCommit,
        [property: JsonPropertyName("host_executable")] string HostExecutable,
        [property: JsonPropertyName("host_executable_sha256")] string HostExecutableSha256,
        [property: JsonPropertyName("host_process_id")] int HostProcessId,
        [property: JsonPropertyName("host_exit_code")] int HostExitCode,
        [property: JsonPropertyName("passed")] bool Passed,
        [property: JsonPropertyName("steps")] IReadOnlyList<ShutdownQualityReportStep> Steps);

    private sealed record ShutdownQualityReportStep(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("elapsed_ms")] double ElapsedMilliseconds,
        [property: JsonPropertyName("timeout_ms")] double? TimeoutMilliseconds,
        [property: JsonPropertyName("error_category")] string ErrorCategory,
        [property: JsonPropertyName("late_completion_status")] string LateCompletionStatus,
        [property: JsonPropertyName("late_completion_elapsed_ms")] double? LateCompletionElapsedMilliseconds);
}
