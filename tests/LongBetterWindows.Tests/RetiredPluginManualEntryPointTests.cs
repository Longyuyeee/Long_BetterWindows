using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class RetiredPluginManualEntryPointTests
{
    private static readonly string[] RetiredEntryPoints =
    [
        "plan-next-plugin-validation.ps1",
        "start-plugin-validation.ps1",
        "complete-plugin-validation.ps1",
        "approve-plugin-manual-evidence.ps1",
    ];

    [Fact]
    public void RetiredManualEntryPoints_AreAbsentFromRepositoryRoot()
    {
        var root = FindRepositoryRoot();

        Assert.All(
            RetiredEntryPoints,
            name => Assert.False(
                File.Exists(Path.Combine(root, name)),
                $"Retired manual entry point was restored: {name}"));
    }

    [Fact]
    public void ActiveGuides_DoNotAdvertiseRetiredManualEntryPoints()
    {
        var root = FindRepositoryRoot();
        var activeGuides = new[]
        {
            "docs/脚本使用说明.md",
            "docs/用户最终验收操作手册.md",
            "docs/插件人工证据验收.md",
            "docs/插件正向功能验收矩阵_2026-07-27.md",
            "docs/plugin-manual-approvals/README.md",
        };

        foreach (var guide in activeGuides)
        {
            var source = File.ReadAllText(Path.Combine(
                root,
                guide.Replace('/', Path.DirectorySeparatorChar)));
            Assert.All(
                RetiredEntryPoints,
                entryPoint => Assert.DoesNotContain(entryPoint, source));
        }
    }

    [Fact]
    public void MatrixVerifier_RemainsIndependentOfManualReceipts()
    {
        var source = Read("verify-plugin-positive-matrix.ps1");

        Assert.DoesNotContain("ApprovalDirectory", source);
        Assert.DoesNotContain("plugin-manual-approvals", source);
        Assert.DoesNotContain("Test-ApprovalReceipt", source);
        Assert.DoesNotContain("reviewer", source);
        Assert.Contains("approval_receipt_count = 0", source);
        Assert.Contains("stale_approval_receipt_count = 0", source);
    }

    [Fact]
    public void EvidenceIo_NormalizesTextLineEndingsBeforeHashing()
    {
        var source = Read("release-evidence-io.ps1");

        Assert.Contains("function Get-NormalizedTextSha256", source);
        Assert.Contains("Replace(\"`r`n\", \"`n\")", source);
        Assert.Contains("Replace(\"`r\", \"`n\")", source);
        Assert.Contains("UTF8Encoding]::new($false)", source);
    }

    [Fact]
    public async Task RealRepositoryMatrix_RunsWithoutApprovalInputs()
    {
        var root = FindRepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, "verify-plugin-positive-matrix.ps1"));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Unable to start matrix verifier.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await outputTask;
        var error = await errorTask;

        Assert.True(
            process.ExitCode == 0,
            $"Exit {process.ExitCode}\nSTDOUT:\n{output}\nSTDERR:\n{error}");
        using var report = JsonDocument.Parse(output);
        var result = report.RootElement;
        Assert.True(result.GetProperty("contract_valid").GetBoolean());
        Assert.Equal(25, result.GetProperty("plugin_count").GetInt32());
        Assert.Equal(42, result.GetProperty("command_count").GetInt32());
        Assert.Equal(25, result.GetProperty("acceptance_scenario_count").GetInt32());
        Assert.Equal(87, result.GetProperty("automated_gate_count").GetInt32());
        Assert.Equal(87, result.GetProperty("passed_gate_count").GetInt32());
        Assert.Equal(0, result.GetProperty("failed_gate_count").GetInt32());
        Assert.Equal(0, result.GetProperty("not_run_gate_count").GetInt32());
        Assert.Equal(0, result.GetProperty("approval_receipt_count").GetInt32());
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
