using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace LongBetterWindows.Tests;

public sealed class FinalClosureAuditScriptTests
{
    private static readonly Lazy<JsonSchema> ReportSchema = new(() =>
    {
        var root = FindRepositoryRoot();
        return JsonSchema.FromText(File.ReadAllText(Path.Combine(
            root,
            "schemas",
            "final-closure-report.schema.json")));
    });

    [Fact]
    public async Task Audit_ReportsCompleteAutomatedGateSetUsingSchemaV2()
    {
        var root = FindRepositoryRoot();
        var result = await RunAuditAsync(root);

        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.StandardOutput);
        AssertSchemaValid(report);
        var document = report.RootElement;
        Assert.Equal(2, document.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "final_closure",
            document.GetProperty("classification").GetString());
        Assert.True(document.GetProperty("checks_skipped").GetBoolean());
        Assert.False(document.TryGetProperty("human_validation", out _));
        Assert.False(document.TryGetProperty(
            "ready_for_human_validation",
            out _));

        var matrix = document.GetProperty("plugin_matrix");
        Assert.Equal(2, matrix.GetProperty("schema_version").GetInt32());
        Assert.Equal(25, matrix.GetProperty("plugin_count").GetInt32());
        Assert.Equal(42, matrix.GetProperty("command_count").GetInt32());
        Assert.Equal(87, matrix.GetProperty("automated_gate_count").GetInt32());

        var acceptance = document.GetProperty("automated_acceptance");
        var gates = acceptance.GetProperty("gates").EnumerateArray().ToArray();
        Assert.Equal(94, gates.Length);
        Assert.Equal(
            gates.Length,
            gates.Select(gate => gate.GetProperty("id").GetString())
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            87,
            gates.Count(gate => gate.GetProperty("id").GetString()!
                .StartsWith("plugin-matrix.", StringComparison.Ordinal)));
        Assert.Equal(
            4,
            gates.Count(gate => gate.GetProperty("status").GetString()
                == "not_run"));
        Assert.Contains(
            gates,
            gate => gate.GetProperty("id").GetString()
                    == "plugin-matrix-contract"
                && gate.GetProperty("status").GetString() == "passed");
        AssertCountConsistency(acceptance, gates);
        Assert.True(acceptance.GetProperty("contract_valid").GetBoolean());
        Assert.False(document.GetProperty("release_eligible").GetBoolean());
    }

    [Fact]
    public async Task Audit_PropagatesTamperedPluginGateFailure()
    {
        var root = FindRepositoryRoot();
        var matrixPath = Path.Combine(
            root,
            "docs",
            "plugin-positive-function-matrix.json");
        var matrix = JsonNode.Parse(await File.ReadAllTextAsync(matrixPath))!;
        matrix["plugins"]![0]!["automated_evidence"]![0]!["symbol"] =
            $"a005-missing-symbol-{Guid.NewGuid():N}";
        var tamperedPath = Path.Combine(
            Path.GetTempPath(),
            $"long-a005-matrix-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(tamperedPath, matrix.ToJsonString());
            var result = await RunAuditAsync(root, tamperedPath);

            Assert.Equal(1, result.ExitCode);
            using var report = JsonDocument.Parse(result.StandardOutput);
            AssertSchemaValid(report);
            var document = report.RootElement;
            var acceptance = document.GetProperty("automated_acceptance");
            var gates = acceptance.GetProperty("gates")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(2, acceptance.GetProperty("failed_gate_count").GetInt32());
            Assert.Contains(
                gates,
                gate => gate.GetProperty("id").GetString()
                        == "plugin-matrix-contract"
                    && gate.GetProperty("status").GetString() == "failed");
            Assert.Contains(
                gates,
                gate => gate.GetProperty("id").GetString()!
                        .StartsWith("plugin-matrix.", StringComparison.Ordinal)
                    && gate.GetProperty("status").GetString() == "failed");
            Assert.False(document.GetProperty("release_eligible").GetBoolean());
            AssertCountConsistency(acceptance, gates);
        }
        finally
        {
            File.Delete(tamperedPath);
        }
    }

    [Fact]
    public async Task Audit_RequireReleaseEligibleRejectsIncompleteRun()
    {
        var root = FindRepositoryRoot();
        var result = await RunAuditAsync(root, requireReleaseEligible: true);

        Assert.Equal(2, result.ExitCode);
        using var report = JsonDocument.Parse(result.StandardOutput);
        AssertSchemaValid(report);
        Assert.False(report.RootElement
            .GetProperty("release_eligible")
            .GetBoolean());
        Assert.True(report.RootElement
            .GetProperty("automated_acceptance")
            .GetProperty("not_run_gate_count")
            .GetInt32() > 0);
    }

    [Fact]
    public void Audit_HasNoActiveHumanApprovalContract()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "verify-final-closure.ps1"));

        Assert.Contains("automated_acceptance", source);
        Assert.Contains("Get-AutomatedReleaseEligibility", source);
        Assert.Contains("verify-plugin-positive-matrix.ps1", source);
        Assert.Contains("capture-native-performance-evidence.ps1", source);
        Assert.Contains("verify-lpwp-compatibility.ps1", source);
        Assert.DoesNotContain("human_validation", source);
        Assert.DoesNotContain("manual", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewer", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approval_receipt", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process -Verb RunAs", source);
    }

    private static void AssertSchemaValid(JsonDocument report)
    {
        var evaluation = ReportSchema.Value.Evaluate(
            report.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(
            evaluation.IsValid,
            JsonSerializer.Serialize(evaluation.Details));
    }

    private static void AssertCountConsistency(
        JsonElement acceptance,
        JsonElement[] gates)
    {
        Assert.Equal(
            gates.Length,
            acceptance.GetProperty("automated_gate_count").GetInt32());
        var classified =
            acceptance.GetProperty("passed_gate_count").GetInt32()
            + acceptance.GetProperty("failed_gate_count").GetInt32()
            + acceptance.GetProperty("environment_blocked_gate_count").GetInt32()
            + acceptance.GetProperty("not_run_gate_count").GetInt32()
            + acceptance.GetProperty("not_applicable_gate_count").GetInt32();
        Assert.Equal(gates.Length, classified);
    }

    private static async Task<ProcessResult> RunAuditAsync(
        string root,
        string? matrixPath = null,
        bool requireReleaseEligible = false)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = root,
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
        if (matrixPath is not null)
        {
            start.ArgumentList.Add("-PluginMatrixPath");
            start.ArgumentList.Add(matrixPath);
        }
        if (requireReleaseEligible)
            start.ArgumentList.Add("-RequireReleaseEligible");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Final closure audit did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
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

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
