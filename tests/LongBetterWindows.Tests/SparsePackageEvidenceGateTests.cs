using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class SparsePackageEvidenceGateTests : IDisposable
{
    private const string Commit = "1234567890abcdef1234567890abcdef12345678";
    private const string Thumbprint = "abcdef1234567890abcdef1234567890abcdef12";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LongBetterWindows.SparseEvidence.Tests",
        Guid.NewGuid().ToString("N"));

    public SparsePackageEvidenceGateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ApprovalAndVerification_AcceptAnUntamperedIndependentReview()
    {
        CreatePendingEvidence();

        var approval = await RunAsync(
            "approve-sparse-package-explorer-evidence.ps1",
            "-EvidenceDirectory", _root,
            "-ExpectedSourceCommit", Commit,
            "-ExpectedCertificateThumbprint", Thumbprint,
            "-Reviewer", "independent-reviewer",
            "-ReviewNotes", "Reviewed all Explorer interaction screenshots.",
            "-ConfirmSelectionPrimaryMenu",
            "-ConfirmBackgroundPrimaryMenu",
            "-ConfirmCorrectNoteTarget",
            "-ConfirmExplorerStable",
            "-ConfirmUninstallRemovedMenu");
        Assert.Equal(0, approval.ExitCode);

        var summaryPath = Path.Combine(_root, "verification.json");
        var verification = await RunAsync(
            "verify-sparse-package-explorer-evidence.ps1",
            "-EvidenceDirectory", _root,
            "-ExpectedSourceCommit", Commit,
            "-ExpectedCertificateThumbprint", Thumbprint,
            "-OutputPath", summaryPath);

        Assert.Equal(0, verification.ExitCode);
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        Assert.True(summary.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(
            "independent-reviewer",
            summary.RootElement.GetProperty("reviewer").GetString());
    }

    [Fact]
    public async Task Verification_RejectsEvidenceFileChangedAfterApproval()
    {
        var payloadPath = CreatePendingEvidence();
        var approval = await RunAsync(
            "approve-sparse-package-explorer-evidence.ps1",
            "-EvidenceDirectory", _root,
            "-ExpectedSourceCommit", Commit,
            "-ExpectedCertificateThumbprint", Thumbprint,
            "-Reviewer", "independent-reviewer",
            "-ReviewNotes", "Reviewed all Explorer interaction screenshots.",
            "-ConfirmSelectionPrimaryMenu",
            "-ConfirmBackgroundPrimaryMenu",
            "-ConfirmCorrectNoteTarget",
            "-ConfirmExplorerStable",
            "-ConfirmUninstallRemovedMenu");
        Assert.Equal(0, approval.ExitCode);
        File.AppendAllText(payloadPath, "tampered");

        var summaryPath = Path.Combine(_root, "tampered-verification.json");
        var verification = await RunAsync(
            "verify-sparse-package-explorer-evidence.ps1",
            "-EvidenceDirectory", _root,
            "-ExpectedSourceCommit", Commit,
            "-ExpectedCertificateThumbprint", Thumbprint,
            "-OutputPath", summaryPath);

        Assert.NotEqual(0, verification.ExitCode);
        Assert.False(File.Exists(summaryPath));
    }

    private string CreatePendingEvidence()
    {
        var payloadPath = Path.Combine(_root, "selection-primary-menu.png");
        File.WriteAllText(payloadPath, "fixture");
        var hash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(payloadPath))).ToLowerInvariant();
        var evidence = new
        {
            schema_version = 1,
            classification = "sparse_package_explorer_evidence",
            environment = new
            {
                label = "clean-vm",
                user = "capture-operator",
                operator_asserted_clean_user = true,
            },
            candidate = new
            {
                source_commit = Commit,
                certificate_thumbprint = Thumbprint,
            },
            automated_checks = new
            {
                passed = true,
                signed_package_valid = true,
                clean_build_chain_valid = true,
                package_removed_after_capture = true,
                legacy_menu_state_unchanged = true,
            },
            files = new[]
            {
                new { file = Path.GetFileName(payloadPath), sha256 = hash },
            },
            human_review = new
            {
                status = "pending",
                reviewer = (string?)null,
                reviewed_at = (string?)null,
                notes = (string?)null,
                checklist = new
                {
                    selection_primary_menu = false,
                    background_primary_menu = false,
                    correct_note_target = false,
                    explorer_stable = false,
                    uninstall_removed_menu = false,
                },
            },
        };
        File.WriteAllText(
            Path.Combine(_root, "sparse-package-explorer-evidence.json"),
            JsonSerializer.Serialize(evidence));
        return payloadPath;
    }

    private static async Task<ProcessResult> RunAsync(
        string script,
        params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(FindRepositoryRoot(), script),
        }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await output,
            await error);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
