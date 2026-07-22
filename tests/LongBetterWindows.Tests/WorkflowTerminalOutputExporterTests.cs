using System.IO;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkflowTerminalOutputExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-terminal-output-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportApproved_CreatesExactUtf8FileWithoutBom()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var output = Output("line one\n第二行");
        var target = Path.Combine(_root, "result.txt");
        var review = exporter.Prepare(output, target);

        var result = await exporter.ExportApprovedAsync(output, target, review.Fingerprint);

        Assert.True(review.IsValid, string.Join(" ", review.Issues));
        Assert.True(result.IsSuccess, result.Error);
        var bytes = await File.ReadAllBytesAsync(target);
        Assert.Equal(new UTF8Encoding(false).GetBytes(output.Value), bytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            result.ValueSha256);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExportApproved_RejectsChangedValueOrDestination()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var output = Output("reviewed");
        var target = Path.Combine(_root, "reviewed.txt");
        var review = exporter.Prepare(output, target);

        var changedValue = await exporter.ExportApprovedAsync(
            output with { Value = "changed" },
            target,
            review.Fingerprint);
        var changedDestination = await exporter.ExportApprovedAsync(
            output,
            Path.Combine(_root, "other.txt"),
            review.Fingerprint);

        Assert.False(changedValue.IsSuccess);
        Assert.False(changedDestination.IsSuccess);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ReviewAndResultExposeHashButNotPlaintext()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var output = Output("sensitive-value-42");
        var target = Path.Combine(_root, "redacted.txt");

        var review = exporter.Prepare(output, target);
        var missingApproval = await exporter.ExportApprovedAsync(output, target, string.Empty);

        Assert.True(review.IsValid, string.Join(" ", review.Issues));
        Assert.Equal(64, review.ValueSha256.Length);
        Assert.DoesNotContain(output.Value, review.ToString(), StringComparison.Ordinal);
        Assert.False(missingApproval.IsSuccess);
        Assert.DoesNotContain(output.Value, missingApproval.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task ExportApproved_NeverOverwritesTargetCreatedAfterReview()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var output = Output("private");
        var target = Path.Combine(_root, "existing.txt");
        var review = exporter.Prepare(output, target);
        await File.WriteAllTextAsync(target, "original");

        var result = await exporter.ExportApprovedAsync(output, target, review.Fingerprint);

        Assert.False(result.IsSuccess);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public void Prepare_RejectsMissingDirectoryExistingTargetAndInvalidPathOutput()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var existing = Path.Combine(_root, "existing.txt");
        File.WriteAllText(existing, "original");

        var missingDirectory = exporter.Prepare(
            Output("value"),
            Path.Combine(_root, "missing", "result.txt"));
        var existingTarget = exporter.Prepare(Output("value"), existing);
        var emptyPathOutput = exporter.Prepare(
            Output(" ") with { Type = PluginCommandOutputType.Path },
            Path.Combine(_root, "path.txt"));

        Assert.False(missingDirectory.IsValid);
        Assert.False(existingTarget.IsValid);
        Assert.False(emptyPathOutput.IsValid);
        Assert.All(
            new[] { missingDirectory, existingTarget, emptyPathOutput },
            review => Assert.Empty(review.Fingerprint));
    }

    [Fact]
    public async Task ExportApproved_CancellationLeavesNoTargetOrTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var exporter = new WorkflowTerminalOutputExporter();
        var output = Output("private");
        var target = Path.Combine(_root, "cancelled.txt");
        var review = exporter.Prepare(output, target);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await exporter.ExportApprovedAsync(
            output,
            target,
            review.Fingerprint,
            cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(_root));
    }

    private static WorkflowTerminalOutput Output(string value)
        => new("step-1", "result", PluginCommandOutputType.Text, value);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
