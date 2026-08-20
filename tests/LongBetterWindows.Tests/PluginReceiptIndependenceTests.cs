using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

[Collection(PerformanceSensitiveCollection.Name)]
public sealed class PluginReceiptIndependenceTests
{
    [Fact]
    public async Task Verifier_IgnoresDamagedAndForgedLegacyReceipts()
    {
        var root = FindRepositoryRoot();
        var baseline = await RunVerifierAsync(root);
        Assert.Equal(0, baseline.ExitCode);

        var receiptDirectory = Path.Combine(
            root,
            "docs",
            "plugin-manual-approvals");
        var damagedPath = Path.Combine(
            receiptDirectory,
            $"a003-damaged-{Guid.NewGuid():N}.json");
        var forgedPath = Path.Combine(
            receiptDirectory,
            $"a003-forged-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(receiptDirectory);

        try
        {
            await File.WriteAllTextAsync(damagedPath, "{not-json");
            await File.WriteAllTextAsync(
                forgedPath,
                JsonSerializer.Serialize(new
                {
                    schema_version = 2,
                    plugin_id = "com.long.fake",
                    manual_check_id = "forged",
                    status = "passed",
                    reviewer = "not-a-gate",
                    manifest_sha256 = new string('a', 64),
                    evidence_files = new[]
                    {
                        new
                        {
                            relative_path = "artifacts/quality/fake.json",
                            sha256 = new string('b', 64),
                            size_bytes = 1,
                        },
                    },
                }));

            var disturbed = await RunVerifierAsync(root);
            Assert.Equal(baseline.ExitCode, disturbed.ExitCode);
            AssertEquivalentReports(
                baseline.StandardOutput,
                disturbed.StandardOutput);
        }
        finally
        {
            File.Delete(damagedPath);
            File.Delete(forgedPath);
        }
    }

    private static void AssertEquivalentReports(
        string expectedJson,
        string actualJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        using var actual = JsonDocument.Parse(actualJson);
        var stableProperties = new[]
        {
            "matrix_path",
            "matrix_sha256",
            "source_commit",
            "source_dirty",
            "plugin_count",
            "command_count",
            "automated_evidence_count",
            "required_manual_check_count",
            "approval_receipt_count",
            "stale_approval_receipt_count",
            "pending_or_blocked_manual_count",
            "failed_manual_count",
            "contract_valid",
            "release_eligible",
            "errors",
        };

        foreach (var property in stableProperties)
        {
            Assert.Equal(
                expected.RootElement.GetProperty(property).GetRawText(),
                actual.RootElement.GetProperty(property).GetRawText());
        }
    }

    private static async Task<ProcessResult> RunVerifierAsync(string root)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile",
                     "-ExecutionPolicy",
                     "Bypass",
                     "-File",
                     Path.Combine(root, "verify-plugin-positive-matrix.ps1"),
                     "-MatrixPath",
                     Path.Combine(
                         root,
                         "docs",
                         "plugin-positive-function-matrix.json"),
                     "-SourceRoot",
                     Path.Combine(root, "src"),
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(
                "PowerShell verifier did not start.");
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
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
