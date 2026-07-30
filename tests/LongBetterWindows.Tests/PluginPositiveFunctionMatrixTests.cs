using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LongBetterWindows.Tests;

public sealed class PluginPositiveFunctionMatrixTests
{
    [Fact]
    public void Matrix_CoversExactlyEveryBuiltInPluginAndCommand()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var expectedPluginCount =
            policy.GetProperty("required_plugin_count").GetInt32();
        var expectedCommandCount =
            policy.GetProperty("required_command_count").GetInt32();
        var matrixPlugins = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .ToDictionary(
                plugin => plugin.GetProperty("id").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        var sourcePlugins = LoadSourceManifests(root);

        Assert.Equal(25, expectedPluginCount);
        Assert.Equal(42, expectedCommandCount);
        Assert.Equal(expectedPluginCount, matrixPlugins.Count);
        Assert.Equal(expectedPluginCount, sourcePlugins.Count);
        Assert.Equal(
            sourcePlugins.Keys.Order(StringComparer.OrdinalIgnoreCase),
            matrixPlugins.Keys.Order(StringComparer.OrdinalIgnoreCase));

        var commandCount = 0;
        foreach (var (pluginId, sourceManifest) in sourcePlugins)
        {
            var matrixPlugin = matrixPlugins[pluginId];
            var sourceCommands = ReadCommands(sourceManifest.RootElement);
            var matrixCommands = matrixPlugin
                .GetProperty("commands")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(sourceCommands, matrixCommands);
            commandCount += matrixCommands.Length;

            var entryPoint = sourceManifest.RootElement
                .GetProperty("entry_point")
                .GetString();
            var expectedRuntime = string.Equals(
                Path.GetExtension(entryPoint),
                ".dll",
                StringComparison.OrdinalIgnoreCase)
                ? "native"
                : "web";
            Assert.Equal(
                expectedRuntime,
                matrixPlugin.GetProperty("runtime").GetString());
            Assert.Contains(
                matrixPlugin.GetProperty("risk").GetString(),
                new[] { "low", "medium", "high", "critical" });
        }
        Assert.Equal(expectedCommandCount, commandCount);
    }

    [Fact]
    public void Matrix_AutomatedEvidenceResolvesToExistingSymbols()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);

        foreach (var plugin in matrix.RootElement
                     .GetProperty("plugins")
                     .EnumerateArray())
        {
            var pluginId = plugin.GetProperty("id").GetString();
            var evidenceItems = plugin
                .GetProperty("automated_evidence")
                .EnumerateArray()
                .ToArray();
            Assert.NotEmpty(evidenceItems);
            foreach (var evidence in evidenceItems)
            {
                var path = Path.GetFullPath(
                    Path.Combine(
                        root,
                        evidence.GetProperty("path").GetString()!));
                Assert.True(
                    File.Exists(path),
                    $"Evidence file is missing for {pluginId}: {path}");
                var symbol = evidence.GetProperty("symbol").GetString();
                Assert.False(string.IsNullOrWhiteSpace(symbol));
                Assert.Contains(
                    symbol!,
                    File.ReadAllText(path),
                    StringComparison.Ordinal);
                Assert.Contains(
                    evidence.GetProperty("level").GetString(),
                    new[] { "contract", "component", "integration" });
            }
        }
    }

    [Fact]
    public void Matrix_ManualChecksCoverEveryCommandAndRemainAuditable()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var requiredChecks = 0;
        var pendingChecks = 0;

        foreach (var plugin in matrix.RootElement
                     .GetProperty("plugins")
                     .EnumerateArray())
        {
            var pluginId = plugin.GetProperty("id").GetString();
            var commands = plugin.GetProperty("commands")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var manualIds = new HashSet<string>(StringComparer.Ordinal);
            var manualChecks = plugin.GetProperty("manual_checks")
                .EnumerateArray()
                .ToArray();
            Assert.NotEmpty(manualChecks);

            foreach (var manualCheck in manualChecks)
            {
                var manualId = manualCheck.GetProperty("id").GetString()!;
                Assert.True(
                    manualIds.Add(manualId),
                    $"Duplicate manual id: {pluginId}/{manualId}");
                foreach (var command in manualCheck
                             .GetProperty("commands")
                             .EnumerateArray())
                {
                    var commandId = command.GetString()!;
                    Assert.Contains(commandId, commands);
                    covered.Add(commandId);
                }

                var status = manualCheck
                    .GetProperty("status")
                    .GetString();
                Assert.Contains(
                    status,
                    new[] { "pending", "passed", "failed", "blocked" });
                if (!manualCheck
                        .GetProperty("required_for_release")
                        .GetBoolean())
                {
                    continue;
                }

                requiredChecks++;
                if (status is "pending" or "blocked")
                    pendingChecks++;
                if (status == "passed")
                {
                    AssertPassedEvidence(
                        root,
                        pluginId!,
                        manualId,
                        manualCheck);
                }
            }
            Assert.Equal(
                commands.Order(StringComparer.Ordinal),
                covered.Order(StringComparer.Ordinal));
        }

        Assert.Equal(25, requiredChecks);
        Assert.Equal(25, pendingChecks);
    }

    [Fact]
    public async Task Verifier_ValidatesContractButBlocksReleaseEligibility()
    {
        var root = FindRepositoryRoot();
        var normal = await RunVerifierAsync(
            root,
            requireReleaseEligible: false);

        Assert.Equal(0, normal.ExitCode);
        using var report = JsonDocument.Parse(normal.StandardOutput);
        Assert.True(
            report.RootElement.GetProperty("contract_valid").GetBoolean());
        Assert.False(
            report.RootElement.GetProperty("release_eligible").GetBoolean());
        Assert.Equal(
            25,
            report.RootElement.GetProperty("plugin_count").GetInt32());
        Assert.Equal(
            42,
            report.RootElement.GetProperty("command_count").GetInt32());
        Assert.Equal(
            25,
            report.RootElement
                .GetProperty("pending_or_blocked_manual_count")
                .GetInt32());

        var release = await RunVerifierAsync(
            root,
            requireReleaseEligible: true);
        Assert.Equal(2, release.ExitCode);
    }

    [Fact]
    public void LowRiskCommandCases_CoverEveryLowRiskPluginAndCommand()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var casesPath = Path.GetFullPath(Path.Combine(
            root,
            policy.GetProperty("isolated_command_cases_path").GetString()!));
        using var cases = JsonDocument.Parse(File.ReadAllText(casesPath));
        var lowRiskPlugins = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Where(plugin => plugin.GetProperty("risk").GetString() == "low")
            .ToDictionary(
                plugin => plugin.GetProperty("id").GetString()!,
                plugin => plugin.GetProperty("commands")
                    .EnumerateArray()
                    .Select(command => command.GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
        var covered = cases.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .GroupBy(
                item => item.GetProperty("plugin_id").GetString()!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.GetProperty("command_id").GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            policy.GetProperty("isolated_command_required_plugin_count").GetInt32(),
            lowRiskPlugins.Count);
        Assert.Equal(
            lowRiskPlugins.Keys.Order(StringComparer.OrdinalIgnoreCase),
            covered.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (pluginId, commands) in lowRiskPlugins)
            Assert.Equal(
                commands.Order(StringComparer.Ordinal),
                covered[pluginId].Order(StringComparer.Ordinal));
        Assert.Equal(
            policy.GetProperty("isolated_command_required_command_count").GetInt32(),
            lowRiskPlugins.Values.Sum(commands => commands.Count));
    }

    [Fact]
    public void MediumRiskCommandCases_CoverEveryMediumRiskPluginAndCommand()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var casesPath = Path.GetFullPath(Path.Combine(
            root,
            policy.GetProperty("medium_command_cases_path").GetString()!));
        using var cases = JsonDocument.Parse(File.ReadAllText(casesPath));
        var mediumRiskPlugins = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Where(plugin => plugin.GetProperty("risk").GetString() == "medium")
            .ToDictionary(
                plugin => plugin.GetProperty("id").GetString()!,
                plugin => plugin.GetProperty("commands")
                    .EnumerateArray()
                    .Select(command => command.GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
        var caseItems = cases.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .ToArray();
        var covered = caseItems
            .GroupBy(
                item => item.GetProperty("plugin_id").GetString()!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.GetProperty("command_id").GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            policy.GetProperty("medium_command_required_plugin_count").GetInt32(),
            mediumRiskPlugins.Count);
        Assert.Equal(
            mediumRiskPlugins.Keys.Order(StringComparer.OrdinalIgnoreCase),
            covered.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (pluginId, commands) in mediumRiskPlugins)
            Assert.Equal(
                commands.Order(StringComparer.Ordinal),
                covered[pluginId].Order(StringComparer.Ordinal));
        Assert.Equal(
            policy.GetProperty("medium_command_required_command_count").GetInt32(),
            mediumRiskPlugins.Values.Sum(commands => commands.Count));
        Assert.All(
            caseItems,
            item => Assert.Equal(
                1,
                item.GetProperty("fixture")
                    .GetProperty("schema_version")
                    .GetInt32()));
    }

    [Fact]
    public void HighRiskTransactionCases_CoverDeclaredTemporaryTargets()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var casesPath = Path.GetFullPath(Path.Combine(
            root,
            policy.GetProperty("high_transaction_cases_path").GetString()!));
        using var cases = JsonDocument.Parse(File.ReadAllText(casesPath));
        var declaredPluginIds = policy
            .GetProperty("high_transaction_plugin_ids")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var highRiskPlugins = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Where(plugin =>
                plugin.GetProperty("risk").GetString() == "high"
                && declaredPluginIds.Contains(
                    plugin.GetProperty("id").GetString()!))
            .ToDictionary(
                plugin => plugin.GetProperty("id").GetString()!,
                plugin => plugin.GetProperty("commands")
                    .EnumerateArray()
                    .Select(command => command.GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
        var caseItems = cases.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .ToArray();
        var covered = caseItems
            .GroupBy(
                item => item.GetProperty("plugin_id").GetString()!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.GetProperty("command_id").GetString()!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            policy.GetProperty("high_transaction_required_plugin_count")
                .GetInt32(),
            declaredPluginIds.Count);
        Assert.Equal(
            declaredPluginIds.Order(StringComparer.OrdinalIgnoreCase),
            highRiskPlugins.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            highRiskPlugins.Keys.Order(StringComparer.OrdinalIgnoreCase),
            covered.Keys.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var (pluginId, commands) in highRiskPlugins)
            Assert.Equal(
                commands.Order(StringComparer.Ordinal),
                covered[pluginId].Order(StringComparer.Ordinal));
        Assert.Equal(
            policy.GetProperty("high_transaction_required_command_count")
                .GetInt32(),
            highRiskPlugins.Values.Sum(commands => commands.Count));
        Assert.All(
            caseItems,
            item => Assert.True(
                item.GetProperty("expected_workspace_unchanged").GetBoolean()));
    }

    [Fact]
    public void QuickLaunchIsolationEvidence_IsBoundToProductionPlugin()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var scriptPath = Path.Combine(
            root,
            policy.GetProperty("quick_launch_isolation_script").GetString()!);
        var testPath = Path.Combine(
            root,
            policy.GetProperty("quick_launch_isolation_test_path").GetString()!);
        var plugin = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Single(item =>
                item.GetProperty("id").GetString()
                == "com.long.quicklaunch");
        var evidence = Assert.Single(
            plugin.GetProperty("automated_evidence").EnumerateArray());

        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(testPath));
        Assert.Equal(
            7,
            policy.GetProperty(
                "quick_launch_isolation_required_case_count").GetInt32());
        Assert.Equal(
            "tests/LongBetterWindows.Tests/QuickLaunchIsolationTests.cs",
            evidence.GetProperty("path").GetString());
        Assert.Equal(
            "LargeDirectorySearch_FindsNestedTargetWithoutMutation",
            evidence.GetProperty("symbol").GetString());
    }

    [Fact]
    public void HighRiskBoundaryGate_CoversEveryDeclaredHighRiskPlugin()
    {
        var root = FindRepositoryRoot();
        using var matrix = LoadMatrix(root);
        var policy = matrix.RootElement.GetProperty("policy");
        var scriptPath = Path.Combine(
            root,
            policy.GetProperty("high_risk_boundary_script").GetString()!);
        var script = File.ReadAllText(scriptPath);
        var declared = policy
            .GetProperty("high_risk_boundary_plugin_ids")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var highRisk = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Where(plugin => plugin.GetProperty("risk").GetString() == "high")
            .Select(plugin => plugin.GetProperty("id").GetString()!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var commandCount = matrix.RootElement
            .GetProperty("plugins")
            .EnumerateArray()
            .Where(plugin => declared.Contains(
                plugin.GetProperty("id").GetString()!,
                StringComparer.OrdinalIgnoreCase))
            .Sum(plugin => plugin.GetProperty("commands").GetArrayLength());

        Assert.True(File.Exists(scriptPath));
        Assert.Equal(highRisk, declared);
        Assert.Equal(
            policy.GetProperty("high_risk_boundary_required_plugin_count")
                .GetInt32(),
            declared.Length);
        Assert.Equal(
            policy.GetProperty("high_risk_boundary_required_command_count")
                .GetInt32(),
            commandCount);
        Assert.Equal(
            43,
            policy.GetProperty("high_risk_boundary_required_case_count")
                .GetInt32());
        Assert.Contains("verify-high-risk-plugin-transactions.ps1", script);
        Assert.Contains("verify-capture-delivery-isolation.ps1", script);
        Assert.Contains("verify-quick-launch-isolation.ps1", script);
        Assert.Contains("FileSystemOrganizationTests", script);
        Assert.Contains("AdsServiceTransactionTests", script);
        Assert.Contains("ProcessServiceTests", script);
        Assert.Contains("ScreenCaptureServiceTests", script);
    }

    private static JsonDocument LoadMatrix(string root)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "plugin-positive-function-matrix.json")));

    private static Dictionary<string, JsonDocument> LoadSourceManifests(
        string root)
    {
        var source = Path.Combine(root, "src");
        return Directory
            .EnumerateDirectories(source)
            .Select(directory => Path.Combine(directory, "manifest.json"))
            .Where(File.Exists)
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .Where(document => document.RootElement
                .GetProperty("id")
                .GetString()!
                .StartsWith(
                    "com.long.",
                    StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                document => document.RootElement
                    .GetProperty("id")
                    .GetString()!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ReadCommands(JsonElement manifest)
        => manifest.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertPassedEvidence(
        string root,
        string pluginId,
        string manualId,
        JsonElement manualCheck)
    {
        var evidencePath = manualCheck
            .GetProperty("evidence_path")
            .GetString();
        var evidenceSha = manualCheck
            .GetProperty("evidence_sha256")
            .GetString();
        Assert.False(
            string.IsNullOrWhiteSpace(evidencePath),
            $"Passed check lacks evidence: {pluginId}/{manualId}");
        Assert.Matches("^[a-fA-F0-9]{64}$", evidenceSha!);
        Assert.True(File.Exists(Path.GetFullPath(
            Path.Combine(root, evidencePath!))));
    }

    private static async Task<ProcessResult> RunVerifierAsync(
        string root,
        bool requireReleaseEligible)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(root, "verify-plugin-positive-matrix.ps1"),
            "-MatrixPath",
            Path.Combine(
                root,
                "docs",
                "plugin-positive-function-matrix.json"),
            "-SourceRoot",
            Path.Combine(root, "src"),
        };
        if (requireReleaseEligible)
            arguments.Add("-RequireReleaseEligible");
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(
                "PowerShell verifier did not start.");
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
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "LongBetterWindows.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
