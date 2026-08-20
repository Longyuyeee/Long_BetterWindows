using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace LongBetterWindows.Tests;

public sealed class AutomatedAcceptanceSchemaTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SchemaPath = Path.Combine(
        RepositoryRoot, "schemas", "automated-acceptance.schema.json");
    private static readonly Lazy<JsonSchema> Schema = new(
        () => JsonSchema.FromText(File.ReadAllText(SchemaPath)));
    private static readonly string[] Statuses =
    [
        "not_run",
        "passed",
        "failed",
        "blocked_environment",
        "not_applicable"
    ];

    [Fact]
    public void Schema_AcceptsEveryDeclaredStatusWithRequiredEvidenceOrReason()
    {
        foreach (var status in Statuses)
        {
            var report = CreateValidReport(status);
            AssertSchemaValid(report);
        }
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("pending_human_observation")]
    [InlineData("blocked")]
    public void Schema_RejectsUndeclaredStatuses(string status)
    {
        var report = CreateValidReport("not_run");
        report["gates"]![0]!["status"] = status;

        AssertSchemaInvalid(report);
    }

    [Theory]
    [InlineData("commit_sha")]
    [InlineData("source_dirty")]
    [InlineData("tested_artifacts")]
    [InlineData("gates")]
    public void Schema_RejectsMissingRootFields(string propertyName)
    {
        var report = CreateValidReport("passed");
        report.Remove(propertyName);

        AssertSchemaInvalid(report);
    }

    [Fact]
    public void Schema_RejectsMissingGateFieldsAndUnboundReasons()
    {
        var missingSummary = CreateValidReport("passed");
        missingSummary["gates"]![0]!.AsObject().Remove("summary");
        AssertSchemaInvalid(missingSummary);

        var blockedWithoutReason = CreateValidReport("blocked_environment");
        blockedWithoutReason["gates"]![0]!.AsObject().Remove("environment_blocker");
        AssertSchemaInvalid(blockedWithoutReason);

        var notApplicableWithoutReason = CreateValidReport("not_applicable");
        notApplicableWithoutReason["gates"]![0]!.AsObject().Remove("not_applicable_reason");
        AssertSchemaInvalid(notApplicableWithoutReason);
    }

    [Fact]
    public void Schema_RejectsMissingPackageOrExecutableAndMalformedHashes()
    {
        var missingExecutable = CreateValidReport("passed");
        missingExecutable["tested_artifacts"]!.AsArray().RemoveAt(1);
        AssertSchemaInvalid(missingExecutable);

        var malformedArtifactHash = CreateValidReport("passed");
        malformedArtifactHash["tested_artifacts"]![0]!["sha256"] = "not-a-sha256";
        AssertSchemaInvalid(malformedArtifactHash);

        var malformedEvidenceHash = CreateValidReport("passed");
        malformedEvidenceHash["gates"]![0]!["evidence"]![0]!["sha256"] = new string('A', 64);
        AssertSchemaInvalid(malformedEvidenceHash);
    }

    [Fact]
    public void SemanticContract_RejectsDuplicateGateAndArtifactIds()
    {
        var duplicateGate = CreateValidReport("passed");
        duplicateGate["gates"]!.AsArray().Add(duplicateGate["gates"]![0]!.DeepClone());
        AssertSchemaValid(duplicateGate);
        Assert.False(HasUniqueIds(duplicateGate, "gates"));

        var duplicateArtifact = CreateValidReport("passed");
        duplicateArtifact["tested_artifacts"]![1]!["id"] = "portable-package";
        AssertSchemaValid(duplicateArtifact);
        Assert.False(HasUniqueIds(duplicateArtifact, "tested_artifacts"));

        var duplicateEvidence = CreateValidReport("passed");
        var evidence = duplicateEvidence["gates"]![0]!["evidence"]!.AsArray();
        var secondEvidence = evidence[0]!.DeepClone();
        secondEvidence["path"] = "artifacts/quality/repository-ci-copy.json";
        evidence.Add(secondEvidence);
        AssertSchemaValid(duplicateEvidence);
        Assert.False(HasUniqueIds(evidence));
    }

    private static JsonObject CreateValidReport(string status)
    {
        var gate = new JsonObject
        {
            ["id"] = "repository-ci",
            ["status"] = status,
            ["summary"] = "Repository CI matched the expected result.",
            ["duration_ms"] = 1250,
            ["evidence"] = new JsonArray()
        };

        if (status is "passed" or "failed")
        {
            gate["evidence"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "repository-ci-report",
                ["kind"] = "json",
                ["path"] = "artifacts/quality/repository-ci.json",
                ["sha256"] = new string('c', 64)
            });
        }
        else if (status == "blocked_environment")
        {
            gate["environment_blocker"] = "Required external service is unavailable.";
        }
        else if (status == "not_applicable")
        {
            gate["not_applicable_reason"] = "The unsigned channel does not use this gate.";
        }

        return new JsonObject
        {
            ["$schema"] = "../../schemas/automated-acceptance.schema.json",
            ["schema_version"] = 1,
            ["generated_at_utc"] = "2026-08-20T01:02:03Z",
            ["commit_sha"] = new string('a', 40),
            ["source_dirty"] = false,
            ["tested_artifacts"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "portable-package",
                    ["kind"] = "package",
                    ["path"] = "artifacts/releases/candidate.zip",
                    ["sha256"] = new string('b', 64)
                },
                new JsonObject
                {
                    ["id"] = "host-executable",
                    ["kind"] = "executable",
                    ["path"] = "publish/LongBetterWindows.Host.exe",
                    ["sha256"] = new string('d', 64)
                }
            },
            ["gates"] = new JsonArray { gate }
        };
    }

    private static bool HasUniqueIds(JsonObject report, string collectionName)
        => HasUniqueIds(report[collectionName]!.AsArray());

    private static bool HasUniqueIds(JsonArray collection)
    {
        var ids = collection
            .Select(item => item!["id"]!.GetValue<string>())
            .ToArray();
        return ids.Length == ids.Distinct(StringComparer.Ordinal).Count();
    }

    private static void AssertSchemaValid(JsonObject instance)
    {
        var result = Schema.Value.Evaluate(JsonSerializer.SerializeToElement(instance));
        Assert.True(result.IsValid, JsonSerializer.Serialize(result));
    }

    private static void AssertSchemaInvalid(JsonObject instance)
        => Assert.False(Schema.Value.Evaluate(JsonSerializer.SerializeToElement(instance)).IsValid);

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
}
