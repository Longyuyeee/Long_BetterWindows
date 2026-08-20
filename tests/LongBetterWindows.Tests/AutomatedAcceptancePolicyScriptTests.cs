using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class AutomatedAcceptancePolicyScriptTests
{
    [Theory]
    [InlineData(3, 3, 0, 0, 0, 0, true, false, true)]
    [InlineData(3, 2, 1, 0, 0, 0, true, false, false)]
    [InlineData(3, 2, 0, 1, 0, 0, true, false, false)]
    [InlineData(3, 2, 0, 0, 1, 0, true, false, false)]
    [InlineData(3, 2, 0, 0, 0, 1, true, false, true)]
    [InlineData(3, 3, 0, 0, 0, 0, true, true, false)]
    [InlineData(3, 3, 0, 0, 0, 0, false, false, false)]
    public async Task Policy_UsesAutomatedGateTruthTable(
        int total,
        int passed,
        int failed,
        int blocked,
        int notRun,
        int notApplicable,
        bool contractValid,
        bool sourceDirty,
        bool expected)
    {
        var result = await RunPolicyAsync(
            total,
            passed,
            failed,
            blocked,
            notRun,
            notApplicable,
            contractValid,
            sourceDirty);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(
            expected,
            report.RootElement.GetProperty("eligible").GetBoolean());
    }

    [Fact]
    public async Task Policy_RejectsInconsistentGateCounts()
    {
        var result = await RunPolicyAsync(
            total: 3,
            passed: 2,
            failed: 0,
            blocked: 0,
            notRun: 0,
            notApplicable: 0,
            contractValid: true,
            sourceDirty: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "gate counts are inconsistent",
            result.StandardError,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunPolicyAsync(
        int total,
        int passed,
        int failed,
        int blocked,
        int notRun,
        int notApplicable,
        bool contractValid,
        bool sourceDirty)
    {
        var root = FindRepositoryRoot();
        var policyPath = Path.Combine(
            root,
            "automated-acceptance-policy.ps1").Replace("'", "''");
        var command = $". '{policyPath}'; " +
            "$eligible = Get-AutomatedReleaseEligibility " +
            $"-AutomatedGateCount {total} " +
            $"-PassedGateCount {passed} " +
            $"-FailedGateCount {failed} " +
            $"-EnvironmentBlockedGateCount {blocked} " +
            $"-NotRunGateCount {notRun} " +
            $"-NotApplicableGateCount {notApplicable} " +
            $"-ContractValid ${contractValid.ToString().ToLowerInvariant()} " +
            $"-SourceDirty ${sourceDirty.ToString().ToLowerInvariant()}; " +
            "[pscustomobject]@{ eligible = [bool]$eligible } | " +
            "ConvertTo-Json -Compress";
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
                     "-Command",
                     command,
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(
                "PowerShell policy process did not start.");
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
