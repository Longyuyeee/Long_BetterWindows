using System.IO;

namespace LongBetterWindows.Tests;

public sealed class WebViewLifecycleScriptTests
{
    [Fact]
    public void Probe_RequiresCleanReleaseHostAndCompleteLifecycleReport()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-webview-lifecycle.ps1"));

        Assert.Contains("--quality-webview-lifecycle-report", source);
        Assert.Contains("git -C $PSScriptRoot status", source);
        Assert.Contains("--porcelain --untracked-files=no", source);
        Assert.Contains("cycle_count -eq 16", source);
        Assert.Contains("$samples.Count -eq 32", source);
        Assert.Contains("$invalidCycles.Count -eq 0", source);
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", source);
        Assert.Contains("growth_passed", source);
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
