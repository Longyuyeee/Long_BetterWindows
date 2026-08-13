using System.IO;

namespace LongBetterWindows.Tests;

public sealed class WebPluginVisibilityLifecycleTests
{
    [Theory]
    [InlineData("HardwareMonitor")]
    [InlineData("PortManager")]
    [InlineData("ClipboardTool")]
    [InlineData("ClipboardHistory")]
    public void PollingPlugin_UsesHostAndDocumentVisibility(string plugin)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            plugin,
            "index.html"));

        Assert.Contains("long?.host?.isVisible?.() !== false", source);
        Assert.Contains("!document.hidden", source);
        Assert.Contains("long-host-visibilitychange", source);
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
