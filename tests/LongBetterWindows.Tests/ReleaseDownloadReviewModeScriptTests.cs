using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class ReleaseDownloadReviewModeScriptTests : IDisposable
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-release-review-mode-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SingleMaintainerApproval_FlowsIntoHashLockedSummary()
    {
        var evidence = WriteEvidence();
        var approval = Path.Combine(_root, "approval.json");
        var summary = Path.Combine(_root, "summary.json");

        var approve = await RunAsync(
            "approve-release-download-evidence.ps1",
            CommonApprovalArguments(evidence, approval).Concat(new[]
            {
                "-ReviewModel", "single_maintainer",
                "-RiskAcceptedBy", "real-maintainer",
                "-RiskAcceptedAt", "2026-08-14T04:00:00Z",
                "-RiskReason", "no_second_machine_or_independent_reviewer",
                "-RiskAcceptedVersion", "1.11.0",
            }));
        Assert.True(approve.ExitCode == 0, approve.Error);

        var verify = await RunAsync(
            "verify-release-download-evidence.ps1",
            new[]
            {
                "-EvidencePath", evidence,
                "-ApprovalPath", approval,
                "-ExpectedSourceCommit", Commit,
                "-ExpectedDistributionChannel", "unsigned",
                "-OutputPath", summary,
            });
        Assert.True(verify.ExitCode == 0, verify.Error);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(summary));
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("single_maintainer", root.GetProperty("review_model").GetString());
        Assert.False(root.GetProperty("independent_review").GetBoolean());
        Assert.Equal(
            "1.11.0",
            root.GetProperty("risk_acceptance")
                .GetProperty("risk_accepted_version")
                .GetString());
    }

    [Fact]
    public async Task DefaultApproval_RejectsSameOperatorAndReviewer()
    {
        var evidence = WriteEvidence();
        var approval = Path.Combine(_root, "strict-approval.json");

        var result = await RunAsync(
            "approve-release-download-evidence.ps1",
            CommonApprovalArguments(evidence, approval));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Independent review requires distinct", result.Error);
        Assert.False(File.Exists(approval));
    }

    private string WriteEvidence()
    {
        Directory.CreateDirectory(_root);
        var packageHash = Convert.ToHexString(SHA256.HashData("package"u8.ToArray()))
            .ToLowerInvariant();
        var path = Path.Combine(_root, "evidence.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            classification = "verified_release_download_provenance",
            passed = true,
            release = new
            {
                version = "1.11.0",
                source_commit = Commit,
                distribution_channel = "unsigned",
                release_eligible = true,
                signed = false,
            },
            package = new { file = "LongBetterWindows.zip", sha256 = packageHash },
            windows_origin = new
            {
                zone_id = 3,
                host = new { scheme = "https", host = "github.com" },
                query_parameters_recorded = false,
            },
        }));
        return path;
    }

    private static IEnumerable<string> CommonApprovalArguments(
        string evidence,
        string approval) => new[]
    {
        "-EvidencePath", evidence,
        "-ExpectedSourceCommit", Commit,
        "-ExpectedDistributionChannel", "unsigned",
        "-Operator", "real-maintainer",
        "-Reviewer", "real-maintainer",
        "-ExtractionMethod", "Explorer extraction",
        "-SmartScreenObservation", "SmartScreen warning observed",
        "-AntivirusObservation", "Antivirus scan completed",
        "-FirstLaunchObservation", "First launch completed",
        "-ReviewNotes", "Single maintainer physical review completed.",
        "-ConfirmExtractionCompleted",
        "-ConfirmExtractedExecutableOriginChecked",
        "-ConfirmSmartScreenObserved",
        "-ConfirmAntivirusObserved",
        "-ConfirmFirstLaunchObserved",
        "-OutputPath", approval,
    };

    private static async Task<ProcessResult> RunAsync(
        string scriptName,
        IEnumerable<string> arguments)
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
            "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(FindRepositoryRoot(), scriptName),
        }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
