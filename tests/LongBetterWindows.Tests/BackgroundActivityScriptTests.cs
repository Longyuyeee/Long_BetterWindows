using System.IO;

namespace LongBetterWindows.Tests;

public sealed class BackgroundActivityScriptTests
{
    [Fact]
    public void Probe_GatesHiddenActivityRestoreAndCleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-background-plugin-activity.ps1"));

        Assert.Contains("--quality-background-activity-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("hidden_host_state", source);
        Assert.Contains("restored_host_state", source);
        Assert.Contains("hidden_api_calls", source);
        Assert.Contains("$plugins.Count -eq 3", source);
        Assert.Contains("$combinedSamples.Count -eq 6", source);
        Assert.Contains("combined.all_hosts_hidden", source);
        Assert.Contains("combined.growth.passed", source);
        Assert.Contains("combined.resource_trend.passed", source);
        Assert.Contains("schema_version -eq 6", source);
        Assert.Contains("$mixedCycles.Count -eq 4", source);
        Assert.Contains("mixed.cleanup_passed", source);
        Assert.Contains("mixed.growth.passed", source);
        Assert.Contains("cleanup_passed", source);
        Assert.Contains("cpu_core_percent -gt 6", source);
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "LongBetterWindows.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
