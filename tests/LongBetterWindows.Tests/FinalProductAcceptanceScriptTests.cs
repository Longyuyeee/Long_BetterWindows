using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Json.Schema;

namespace LongBetterWindows.Tests;

public sealed class FinalProductAcceptanceScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "long-a006-tests",
        Guid.NewGuid().ToString("N"));
    private static readonly Lazy<JsonSchema> ReportSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(
            FindRoot(),
            "schemas",
            "final-product-acceptance-report.schema.json"))));

    [Fact]
    public void Aggregator_HasNoActiveHumanApprovalContract()
    {
        var source = Read("verify-final-product-acceptance.ps1");

        Assert.Contains("FinalClosureReportPath", source);
        Assert.Contains("automated_final_product_acceptance", source);
        Assert.Contains("Get-AutomatedReleaseEligibility", source);
        Assert.Contains("Write-NewJsonFileAtomically", source);
        Assert.Contains(".sources", source);
        Assert.DoesNotContain("ApprovalDirectory", source);
        Assert.DoesNotContain("reviewer", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receipt", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approved_validation", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ls-files --error-unmatch", source);
    }

    [Fact]
    public async Task Aggregator_PackagesHashLockedAutomatedClosure()
    {
        var closure = await WriteClosureFixtureAsync(blocked: true);
        var output = Path.Combine(_root, "product.json");

        var result = await RunAggregatorAsync(closure, output);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        var document = report.RootElement;
        Assert.Equal(
            "blocked_environment",
            document.GetProperty("acceptance_status").GetString());
        Assert.Equal(94, document.GetProperty("automated_gate_count").GetInt32());
        Assert.Equal(93, document.GetProperty("passed_gate_count").GetInt32());
        Assert.Equal(
            1,
            document.GetProperty("environment_blocked_gate_count").GetInt32());
        Assert.False(document.GetProperty("release_eligible").GetBoolean());
        var sourceEntry = document.GetProperty("final_closure");
        var portablePath = Path.Combine(
            _root,
            sourceEntry.GetProperty("file").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(portablePath));
        Assert.Equal(
            sourceEntry.GetProperty("sha256").GetString(),
            Hash(await File.ReadAllBytesAsync(portablePath)));
    }

    [Fact]
    public async Task Aggregator_PropagatesFailedGateWithoutWritingPackage()
    {
        var closure = await WriteClosureFixtureAsync(failed: true);
        var output = Path.Combine(_root, "failed-product.json");

        var result = await RunAggregatorAsync(closure, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("failed automated gates", result.Error);
        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(Path.Combine(_root, "failed-product.sources")));
    }

    [Fact]
    public async Task Aggregator_RejectsTamperedReleaseHostIdentity()
    {
        var closure = await WriteClosureFixtureAsync(tamperHost: true);
        var output = Path.Combine(_root, "tampered-host.json");

        var result = await RunAggregatorAsync(closure, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("host identity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Aggregator_RequireReleaseEligibleReturnsTwoForEnvironmentBlocker()
    {
        var closure = await WriteClosureFixtureAsync(blocked: true);
        var output = Path.Combine(_root, "required-product.json");

        var result = await RunAggregatorAsync(
            closure,
            output,
            requireReleaseEligible: true);

        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(output));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        AssertSchemaValid(report);
        Assert.False(report.RootElement.GetProperty("release_eligible").GetBoolean());
    }

    [Fact]
    public async Task Aggregator_RejectsRealSkippedFinalClosureReport()
    {
        Directory.CreateDirectory(_root);
        var closure = Path.Combine(_root, "real-skipped-closure.json");
        var closureResult = await RunPowerShellAsync(new[]
        {
            "-File",
            Path.Combine(FindRoot(), "verify-final-closure.ps1"),
            "-SkipBuildAndTests",
            "-AllowDirty",
            "-OutputPath",
            closure,
        });
        Assert.Equal(0, closureResult.ExitCode);

        var output = Path.Combine(_root, "skipped-product.json");
        var result = await RunAggregatorAsync(closure, output);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not run", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    private async Task<string> WriteClosureFixtureAsync(
        bool blocked = false,
        bool failed = false,
        bool tamperHost = false)
    {
        Directory.CreateDirectory(_root);
        var repository = FindRoot();
        var commit = await GitAsync("rev-parse", "HEAD");
        var dirty = (await GitAsync("status", "--porcelain", "--untracked-files=no"))
            .Length > 0;
        var hostPath = Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "bin",
            "Release",
            "net8.0-windows",
            "LongBetterWindows.Host.exe");
        Assert.True(File.Exists(hostPath));
        var hostHash = tamperHost
            ? new string('0', 64)
            : Hash(await File.ReadAllBytesAsync(hostPath));
        var gates = new List<object>();
        for (var index = 0; index < 87; index++)
            gates.Add(Gate($"plugin-matrix.fixture-{index:000}", "passed"));
        gates.Add(Gate("dependency-restore", "passed"));
        gates.Add(Gate("release-build", "passed"));
        gates.Add(Gate("full-automated-tests", "passed"));
        gates.Add(Gate("plugin-matrix-contract", "passed"));
        gates.Add(Gate("release-host-executable", "passed"));
        gates.Add(blocked
            ? Gate("native-performance-preflight", "blocked_environment")
            : failed
                ? Gate("native-performance-preflight", "failed")
                : Gate("native-performance-preflight", "passed"));
        gates.Add(Gate("lpwp-compatibility", "passed"));
        var passed = gates.Count(gate =>
            JsonSerializer.Serialize(gate).Contains("\"status\":\"passed\"", StringComparison.Ordinal));
        var failedCount = failed ? 1 : 0;
        var blockedCount = blocked ? 1 : 0;
        var eligible = !dirty && failedCount == 0 && blockedCount == 0;
        var closure = new
        {
            schema_version = 2,
            classification = "final_closure",
            source_commit = commit,
            source_dirty = dirty,
            checks_skipped = false,
            release_host = new { exists = true, path = hostPath, sha256 = hostHash },
            plugin_matrix = new
            {
                schema_version = 2,
                source_commit = commit,
                source_dirty = dirty,
                plugin_count = 25,
                command_count = 42,
                acceptance_scenario_count = 25,
                automated_gate_count = 87,
                passed_gate_count = 87,
                failed_gate_count = 0,
                environment_blocked_gate_count = 0,
                not_run_gate_count = 0,
                not_applicable_gate_count = 0,
                contract_valid = true,
                release_eligible = !dirty,
                report_sha256 = new string('b', 64),
            },
            automated_acceptance = new
            {
                automated_gate_count = 94,
                passed_gate_count = passed,
                failed_gate_count = failedCount,
                environment_blocked_gate_count = blockedCount,
                not_run_gate_count = 0,
                not_applicable_gate_count = 0,
                contract_valid = true,
                gates,
                errors = Array.Empty<string>(),
            },
            release_eligible = eligible,
        };
        var path = Path.Combine(_root, $"closure-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(closure));
        return path;
    }

    private static object Gate(string id, string status)
    {
        var evidence = new[]
        {
            new
            {
                id = "fixture",
                kind = "json",
                path = $"fixture://{id}",
                sha256 = new string('a', 64),
            },
        };
        var gate = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["status"] = status,
            ["summary"] = status == "blocked_environment"
                ? "Fixture environment is unavailable."
                : "Fixture gate result.",
            ["category"] = status == "blocked_environment"
                ? "performance"
                : "test",
            ["evidence"] = evidence,
        };
        if (status == "blocked_environment")
            gate["environment_blocker"] = "fixture prerequisite is unavailable";
        return gate;
    }

    private async Task<ProcessResult> RunAggregatorAsync(
        string closure,
        string output,
        bool requireReleaseEligible = false)
    {
        var arguments = new List<string>
        {
            "-File",
            Path.Combine(FindRoot(), "verify-final-product-acceptance.ps1"),
            "-FinalClosureReportPath",
            closure,
            "-OutputPath",
            output,
            "-ExpectedSourceCommit",
            await GitAsync("rev-parse", "HEAD"),
            "-AllowDirty",
        };
        if (requireReleaseEligible)
            arguments.Add("-RequireReleaseEligible");
        return await RunPowerShellAsync(arguments);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = FindRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<string> GitAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = FindRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        return output.Trim();
    }

    private static void AssertSchemaValid(JsonDocument report)
    {
        var evaluation = ReportSchema.Value.Evaluate(
            report.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, JsonSerializer.Serialize(evaluation.Details));
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string Read(string name) => File.ReadAllText(Path.Combine(FindRoot(), name));

    private static string FindRoot()
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
