using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class FinalClosureAuditScriptTests
{
    [Fact]
    public async Task Audit_ReportsMachineAndHumanClosureSeparately()
    {
        var root = FindRepositoryRoot();
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
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(root, "verify-final-closure.ps1"),
            "-SkipBuildAndTests",
            "-AllowDirty",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"Exit={process.ExitCode}{Environment.NewLine}{await error}");
        using var report = JsonDocument.Parse(await output);
        var document = report.RootElement;
        Assert.Equal(
            "final_closure_readiness",
            document.GetProperty("classification").GetString());
        Assert.True(document.GetProperty("checks_skipped").GetBoolean());
        Assert.False(
            document.GetProperty("ready_for_human_validation").GetBoolean());
        Assert.False(document.GetProperty("release_eligible").GetBoolean());
        Assert.Equal(
            25,
            document.GetProperty("plugin_matrix")
                .GetProperty("plugin_count")
                .GetInt32());
        Assert.Equal(
            42,
            document.GetProperty("plugin_matrix")
                .GetProperty("command_count")
                .GetInt32());
        Assert.Equal(
            7,
            document.GetProperty("human_validation")
                .GetArrayLength());
    }

    [Fact]
    public void Audit_KeepsExternalActionsExplicitAndReadOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-final-closure.ps1"));

        Assert.Contains("capture-native-performance-evidence.ps1", source);
        Assert.Contains("-PreflightOnly", source);
        Assert.Contains("ready_for_human_validation", source);
        Assert.Contains("remaining_human_validation_count", source);
        Assert.Contains("blocked_requires_controlled_credentials", source);
        Assert.DoesNotContain("-ConfirmPassed", source);
        Assert.DoesNotContain("Start-Process -Verb RunAs", source);
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
}
