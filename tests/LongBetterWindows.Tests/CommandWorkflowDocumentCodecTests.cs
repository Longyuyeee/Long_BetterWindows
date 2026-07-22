using System.Text.Json;
using System.Text.Json.Nodes;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class CommandWorkflowDocumentCodecTests
{
    [Fact]
    public void SerializeAndDeserialize_CurrentSchema_RoundTripsNormalizedDefinition()
    {
        var workflow = Workflow("  workflow.local  ");
        var json = CommandWorkflowDocumentCodec.Serialize(
            workflow,
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.LocalManaged, "local-user"));

        var result = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(CommandWorkflowDocumentCodec.CurrentSchemaVersion, ReadSchema(json));
        Assert.Equal("workflow.local", result.Workflow!.Id);
        Assert.Equal(WorkflowDocumentTrustLevel.LocalManaged, result.TrustLevel);
        Assert.Equal(64, result.DefinitionSha256.Length);
        Assert.Null(result.MigratedFromSchemaVersion);
    }

    [Fact]
    public void Deserialize_V1Document_MigratesAsUntrustedLegacyImport()
    {
        var json = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            workflow = Workflow("workflow.legacy"),
        }, SnakeCaseOptions());

        var result = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.MigratedFromSchemaVersion);
        Assert.Equal("legacy-v1", result.Source!.SourceId);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, result.TrustLevel);
    }

    [Fact]
    public void Deserialize_ExternalFileCannotClaimLocalManagedTrust()
    {
        var json = CommandWorkflowDocumentCodec.Serialize(
            Workflow("workflow.claim"),
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.LocalManaged, "claimed-local"));

        var result = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, result.TrustLevel);
    }

    [Fact]
    public void Deserialize_AllowlistedSourceAndExactDefinitionHash_IsTrusted()
    {
        var workflow = Workflow("workflow.curated");
        var hash = CommandWorkflowDocumentCodec.ComputeDefinitionSha256(workflow);
        var policy = new WorkflowSourceTrustPolicy(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["official.templates"] = new[] { hash },
            });
        var json = CommandWorkflowDocumentCodec.Serialize(
            workflow,
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "official.templates"));

        var result = CommandWorkflowDocumentCodec.Deserialize(json, false, policy);

        Assert.Equal(WorkflowDocumentTrustLevel.TrustedSource, result.TrustLevel);
    }

    [Fact]
    public void Deserialize_TrustedSourceWithChangedDefinition_IsUntrusted()
    {
        var trusted = Workflow("workflow.curated") with { Name = "Trusted" };
        var changed = trusted with { Name = "Changed" };
        var policy = new WorkflowSourceTrustPolicy(
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["official.templates"] = new[]
                {
                    CommandWorkflowDocumentCodec.ComputeDefinitionSha256(trusted),
                },
            });
        var json = CommandWorkflowDocumentCodec.Serialize(
            changed,
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "official.templates"));

        var result = CommandWorkflowDocumentCodec.Deserialize(json, false, policy);

        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, result.TrustLevel);
    }

    [Fact]
    public void ComputeDefinitionSha256_IsStableAcrossArgumentInsertionOrder()
    {
        var first = Workflow("workflow.hash", new Dictionary<string, string>
        {
            ["z"] = "last",
            ["a"] = "first",
        });
        var second = Workflow("workflow.hash", new Dictionary<string, string>
        {
            ["a"] = "first",
            ["z"] = "last",
        });

        Assert.Equal(
            CommandWorkflowDocumentCodec.ComputeDefinitionSha256(first),
            CommandWorkflowDocumentCodec.ComputeDefinitionSha256(second));
    }

    [Fact]
    public void ContainsSensitiveInputs_TreatsTextPathsImagesAndArgumentsAsSensitive()
    {
        var text = Workflow("workflow.text") with
        {
            Steps = [Step("step", "private text")],
        };
        var argumentsOnly = Workflow("workflow.arguments", new Dictionary<string, string>
        {
            ["format"] = "png",
        });

        Assert.True(CommandWorkflowDocumentCodec.ContainsSensitiveInputs(text));
        Assert.True(CommandWorkflowDocumentCodec.ContainsSensitiveInputs(argumentsOnly));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Deserialize_UnsupportedSchema_IsRejected(int schemaVersion)
    {
        var json = $"{{\"schema_version\":{schemaVersion},\"workflow\":{{}}}}";

        var result = CommandWorkflowDocumentCodec.Deserialize(json, false);

        Assert.False(result.IsSuccess);
        Assert.Contains("not supported", result.Error);
    }

    [Fact]
    public void Deserialize_InvalidNumericEnum_IsRejected()
    {
        var json = CommandWorkflowDocumentCodec.Serialize(
            Workflow("workflow.invalid-enum"),
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "test"));
        var root = JsonNode.Parse(json)!.AsObject();
        root["workflow"]!["steps"]![0]!["effect"] = 999;

        var result = CommandWorkflowDocumentCodec.Deserialize(root.ToJsonString(), false);

        Assert.False(result.IsSuccess);
        Assert.Contains("effect is invalid", result.Error);
    }

    [Fact]
    public void Deserialize_IncompleteCompensation_IsRejected()
    {
        var workflow = Workflow("workflow.bad-compensation") with
        {
            Steps =
            [
                Step("step") with
                {
                    Compensation = new WorkflowCommand(string.Empty, null),
                },
            ],
        };
        var json = CommandWorkflowDocumentCodec.Serialize(
            workflow,
            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "test"));

        var result = CommandWorkflowDocumentCodec.Deserialize(json, false);

        Assert.False(result.IsSuccess);
        Assert.Contains("compensation is incomplete", result.Error);
    }

    private static CommandWorkflowDefinition Workflow(
        string id,
        IReadOnlyDictionary<string, string>? arguments = null)
        => new(
            id,
            "Workflow",
            WorkflowFailureMode.Stop,
            [Step("step", arguments: arguments)]);

    private static CommandWorkflowStep Step(
        string id,
        string? text = null,
        IReadOnlyDictionary<string, string>? arguments = null)
        => new(
            id,
            WorkflowStepEffect.ReadOnly,
            new WorkflowCommand(
                "plugin:command",
                new PluginCommandInvocation
                {
                    CommandId = "command",
                    Text = text,
                    Arguments = arguments ?? new Dictionary<string, string>(),
                }));

    private static int ReadSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("schema_version").GetInt32();
    }

    private static JsonSerializerOptions SnakeCaseOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
