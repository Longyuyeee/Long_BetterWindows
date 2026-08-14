using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class ReleaseReviewPolicyScriptTests
{
    [Fact]
    public void IndependentReview_IsTheStrictDefaultContract()
    {
        var result = RunPolicy(
            "-ReviewModel independent -CandidateVersion 1.11.0 " +
            "-Operator operator-user -Reviewer reviewer-user");

        Assert.True(result.ExitCode == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("independent", document.RootElement.GetProperty("review_model").GetString());
        Assert.True(document.RootElement.GetProperty("independent_review").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("risk_acceptance").ValueKind);
    }

    [Fact]
    public void SingleMaintainer_RejectsPrereleaseCandidate()
    {
        var result = RunPolicy(SingleMaintainerArguments("1.11.0-rc.8", "1.11.0-rc.8"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("only for a stable semantic version", result.Error);
    }

    [Fact]
    public void SingleMaintainer_RejectsMissingRiskFields()
    {
        var result = RunPolicy(
            "-ReviewModel single_maintainer -CandidateVersion 1.11.0 " +
            "-Operator maintainer -Reviewer maintainer");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("risk acceptance identity", result.Error);
    }

    [Fact]
    public void IndependentReview_RejectsSingleMaintainerRiskFields()
    {
        var result = RunPolicy(
            "-ReviewModel independent -CandidateVersion 1.11.0 " +
            "-Operator operator-user -Reviewer reviewer-user " +
            "-RiskAcceptedBy operator-user");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not include", result.Error);
    }

    private static string SingleMaintainerArguments(string candidate, string acceptedVersion) =>
        $"-ReviewModel single_maintainer -CandidateVersion {candidate} " +
        "-Operator maintainer -Reviewer maintainer -RiskAcceptedBy maintainer " +
        "-RiskAcceptedAt 2026-08-14T04:00:00Z " +
        "-RiskReason no_second_machine_or_independent_reviewer " +
        $"-RiskAcceptedVersion {acceptedVersion}";

    private static ProcessResult RunPolicy(string arguments)
    {
        var script = Path.Combine(FindRepositoryRoot(), "release-review-policy.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var command = $"$ErrorActionPreference='Stop'; . '{script}'; " +
            $"Resolve-LongReleaseReviewPolicy {arguments} | ConvertTo-Json -Depth 4 -Compress";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("PowerShell could not start.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "PowerShell policy evaluation timed out.");
        return new ProcessResult(process.ExitCode, output.Trim(), error.Trim());
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
