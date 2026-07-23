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
        Assert.Equal(WorkflowTerminalOutputExportFailure.None, result.Failure);
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
        Assert.Equal(WorkflowTerminalOutputExportFailure.ReviewChanged, changedValue.Failure);
        Assert.Equal(WorkflowTerminalOutputExportFailure.ReviewChanged, changedDestination.Failure);
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
        Assert.Equal(
            WorkflowTerminalOutputExportFailure.ApprovalMissing,
            missingApproval.Failure);
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
        Assert.Equal(WorkflowTerminalOutputExportFailure.ReviewInvalid, result.Failure);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
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
    public void Prepare_RejectsReparsePointInDestinationHierarchy()
    {
        Directory.CreateDirectory(_root);
        var fileSystem = new ControlledFileSystem
        {
            ReparsePointPath = _root,
        };
        var exporter = new WorkflowTerminalOutputExporter(fileSystem);

        var review = exporter.Prepare(Output("sensitive"), Path.Combine(_root, "result.txt"));

        Assert.False(review.IsValid);
        Assert.Empty(review.Fingerprint);
        Assert.Contains(review.Issues, issue => issue.Contains("reparse-point", StringComparison.Ordinal));
        Assert.DoesNotContain("sensitive", review.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ExportApproved_TargetCreatedDuringWriteIsNotOverwritten()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "raced.txt");
        var fileSystem = new ControlledFileSystem
        {
            BeforeMove = (_, destination) => File.WriteAllText(destination, "original"),
        };
        var exporter = new WorkflowTerminalOutputExporter(fileSystem);
        var output = Output("sensitive");
        var review = exporter.Prepare(output, target);

        var result = await exporter.ExportApprovedAsync(output, target, review.Fingerprint);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkflowTerminalOutputExportFailure.IoFailure, result.Failure);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.DoesNotContain(output.Value, result.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task ExportApproved_AccessDeniedCleansPartialTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var fileSystem = new ControlledFileSystem
        {
            DenyWriteAfterCreatingFile = true,
        };
        var exporter = new WorkflowTerminalOutputExporter(fileSystem);
        var output = Output("sensitive");
        var target = Path.Combine(_root, "denied.txt");
        var review = exporter.Prepare(output, target);

        var result = await exporter.ExportApprovedAsync(output, target, review.Fingerprint);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkflowTerminalOutputExportFailure.AccessDenied, result.Failure);
        Assert.False(File.Exists(target));
        Assert.DoesNotContain(output.Value, result.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root));
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
        Assert.Equal(WorkflowTerminalOutputExportFailure.Cancelled, result.Failure);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(_root));
    }

    private static WorkflowTerminalOutput Output(string value)
        => new("step-1", "result", PluginCommandOutputType.Text, value);

    private sealed class ControlledFileSystem : WorkflowTerminalOutputFileSystem
    {
        internal string? ReparsePointPath { get; init; }
        internal bool DenyWriteAfterCreatingFile { get; init; }
        internal Action<string, string>? BeforeMove { get; init; }

        internal override FileAttributes GetDirectoryAttributes(string path)
        {
            var attributes = base.GetDirectoryAttributes(path);
            return string.Equals(path, ReparsePointPath, StringComparison.OrdinalIgnoreCase)
                ? attributes | FileAttributes.ReparsePoint
                : attributes;
        }

        internal override async Task WriteNewAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            if (!DenyWriteAfterCreatingFile)
            {
                await base.WriteNewAsync(path, bytes, cancellationToken);
                return;
            }

            await File.WriteAllTextAsync(path, "partial", cancellationToken);
            throw new UnauthorizedAccessException("sensitive-value-42");
        }

        internal override void MoveNew(string sourcePath, string destinationPath)
        {
            BeforeMove?.Invoke(sourcePath, destinationPath);
            base.MoveNew(sourcePath, destinationPath);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
