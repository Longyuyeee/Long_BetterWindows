using System.IO;

namespace LongBetterWindows.Tests;

public sealed class HostProcessCleanupScriptTests
{
    [Fact]
    public void Probe_TracksOnlyHostDescendantsAcrossExit()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-host-process-cleanup.ps1"));

        Assert.Contains("Get-CimInstance Win32_Process", source);
        Assert.Contains("Get-Descendants", source);
        Assert.Contains("$hostProcess.Id", source);
        Assert.Contains("ProcessId)|$($ProcessInfo.CreationDate", source);
        Assert.Contains("observed_webview2_count", source);
        Assert.Contains("observed_plugin_worker_count", source);
        Assert.Contains("remaining_descendant_count", source);
        Assert.Contains("host_executable_sha256", source);
        Assert.Contains("--quality-open-plugin-runtime", source);
        Assert.Contains("com.long.base64", source);
        Assert.Contains("--quality-idle-ms", source);
        Assert.Contains("taskkill.exe /PID $Process.Id /T /F", source);
        Assert.DoesNotContain("Get-Process -Name msedgewebview2", source);
        Assert.DoesNotContain("Stop-Process -Name", source);
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
