using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class OneClickEntryPointTests
{
    [Fact]
    public void BatchEntryPoints_AreAsciiWrappersWithoutStaleVersions()
    {
        var root = FindRepositoryRoot();
        var start = File.ReadAllText(Path.Combine(root, "启动.bat"));
        var development = File.ReadAllText(
            Path.Combine(root, "开发模式.bat"));
        var package = File.ReadAllText(
            Path.Combine(root, "打包发布.bat"));

        Assert.All(
            new[] { start, development, package },
            source =>
            {
                Assert.All(source, character => Assert.True(character <= 127));
                Assert.Contains("%~dp0", source);
                Assert.DoesNotContain("1.8", source);
                Assert.DoesNotContain("rmdir", source);
                Assert.DoesNotContain("xcopy", source);
            });
        Assert.Contains("start-long.ps1", start);
        Assert.Contains("start-long.ps1", development);
        Assert.Contains("-Configuration Debug -Wait", development);
        Assert.Contains("package-long.ps1", package);
        Assert.Contains("-OpenOutput", package);
    }

    [Fact]
    public void PowerShellEntryPoints_PreserveSafeReleaseBoundaries()
    {
        var root = FindRepositoryRoot();
        var start = File.ReadAllText(
            Path.Combine(root, "start-long.ps1"));
        var package = File.ReadAllText(
            Path.Combine(root, "package-long.ps1"));

        Assert.Contains("LongBetterWindows.sln", start);
        Assert.Contains("LongBetterWindows.Host.exe", start);
        Assert.Contains("Start-Process @startParameters", start);
        Assert.Contains("QualityIdleMilliseconds", start);
        Assert.Contains("PreflightOnly", start);
        Assert.Contains("<Version>([^<]+)</Version>", package);
        Assert.Contains("release.ps1", package);
        Assert.Contains("release-manifest.json", package);
        Assert.Contains("SHA256SUMS.txt", package);
        Assert.Contains("sourceDirty", package);
        Assert.Contains("Inno Setup 6", package);
        Assert.Contains("ReplaceExisting", package);
        Assert.Contains("Manual release gates still apply", package);
    }

    [Fact]
    public async Task Preflights_ResolveRepositoryAndProjectVersion()
    {
        var root = FindRepositoryRoot();
        using var start = await RunPowerShellAsync(
            root,
            "start-long.ps1",
            "-PreflightOnly");
        using var package = await RunPowerShellAsync(
            root,
            "package-long.ps1",
            "-PreflightOnly",
            "-AllowDirty");

        Assert.Equal(
            "long_assistant_start_preflight",
            start.RootElement.GetProperty("classification").GetString());
        Assert.True(
            start.RootElement.GetProperty("dotnet_available").GetBoolean());
        Assert.True(
            start.RootElement.GetProperty("project_available").GetBoolean());
        Assert.Equal(
            "long_assistant_package_preflight",
            package.RootElement.GetProperty("classification").GetString());
        Assert.Equal(
            "1.11.0-rc.6",
            package.RootElement.GetProperty("version").GetString());
        Assert.True(
            package.RootElement.GetProperty("release_script_available")
                .GetBoolean());
    }

    private static async Task<JsonDocument> RunPowerShellAsync(
        string root,
        params string[] arguments)
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
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(root, arguments[0]),
        }.Concat(arguments.Skip(1)))
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
        return JsonDocument.Parse(await output);
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
