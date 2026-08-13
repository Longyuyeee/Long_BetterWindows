using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ShutdownQualityReportWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-shutdown-report-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteNew_BindsEvidenceAndOmitsExceptionDetails()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "host.exe");
        await File.WriteAllBytesAsync(executable, [1, 2, 3, 4]);
        var output = Path.Combine(_root, "evidence", "shutdown.json");
        var sensitive = new InvalidOperationException("private failure detail");
        var lateCompletion = Task.FromResult(new ShutdownLateCompletion(
            ShutdownStepStatus.Passed,
            52.4,
            ShutdownErrorCategory.None));
        var results = new[]
        {
            new ShutdownStepResult(
                "broker",
                ShutdownStepStatus.Passed,
                1.2,
                5000,
                ShutdownErrorCategory.None),
            new ShutdownStepResult(
                "plugins",
                ShutdownStepStatus.TimedOut,
                30.4,
                30,
                ShutdownErrorCategory.Timeout,
                sensitive,
                lateCompletion),
            new ShutdownStepResult(
                "plugin_runtime",
                ShutdownStepStatus.Passed,
                0.2,
                null,
                ShutdownErrorCategory.None),
            new ShutdownStepResult(
                "host_services",
                ShutdownStepStatus.Passed,
                0.3,
                null,
                ShutdownErrorCategory.None),
        };

        ShutdownQualityReportWriter.WriteNew(
            output,
            "abc123",
            executable,
            42,
            7,
            results);

        var json = await File.ReadAllTextAsync(output);
        Assert.DoesNotContain(sensitive.Message, json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("host_shutdown", root.GetProperty("classification").GetString());
        Assert.Equal("abc123", root.GetProperty("source_commit").GetString());
        Assert.Equal(42, root.GetProperty("host_process_id").GetInt32());
        Assert.Equal(7, root.GetProperty("host_exit_code").GetInt32());
        Assert.False(root.GetProperty("passed").GetBoolean());
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData([1, 2, 3, 4])).ToLowerInvariant(),
            root.GetProperty("host_executable_sha256").GetString());
        var steps = root.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(
            ["broker", "plugins", "plugin_runtime", "host_services"],
            steps.Select(step => step.GetProperty("name").GetString()));
        Assert.Equal("timed_out", steps[1].GetProperty("status").GetString());
        Assert.Equal("timeout", steps[1].GetProperty("error_category").GetString());
        Assert.Equal(
            "passed",
            steps[1].GetProperty("late_completion_status").GetString());
    }

    [Fact]
    public async Task WriteNew_RefusesToOverwriteExistingEvidence()
    {
        Directory.CreateDirectory(_root);
        var executable = Path.Combine(_root, "host.exe");
        await File.WriteAllBytesAsync(executable, [1]);
        var output = Path.Combine(_root, "shutdown.json");
        await File.WriteAllTextAsync(output, "original");

        Assert.Throws<IOException>(() =>
        {
            ShutdownQualityReportWriter.WriteNew(
                output,
                "abc123",
                executable,
                1,
                0,
                Array.Empty<ShutdownStepResult>());
        });
        Assert.Equal("original", await File.ReadAllTextAsync(output));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
