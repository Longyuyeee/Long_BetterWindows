using System.IO;

namespace LongBetterWindows.Tests;

public sealed class TrayRecoveryScriptTests
{
    [Fact]
    public void Probe_GatesProductionTrayPathVisibilityGrowthAndCleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-tray-recovery.ps1"));

        Assert.Contains("--quality-tray-recovery-report", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("close_intercepted", source);
        Assert.Contains("hidden_host_state", source);
        Assert.Contains("primary_action_handled", source);
        Assert.Contains("restored_host_state", source);
        Assert.Contains("resource_trend.passed", source);
        Assert.Contains("warm_baseline_cycle -eq 1", source);
        Assert.Contains("cleanup_passed", source);
        Assert.Contains("$cycles.Count -eq 8", source);
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
