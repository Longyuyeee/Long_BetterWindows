using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class WorkflowInvocationEditorModelTests
{
    [Fact]
    public void InputType_UpdatesVisibleEditorSections()
    {
        var model = Model();
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        model.InputType = AcceptedInputType.Files;

        Assert.True(model.ShowText);
        Assert.True(model.ShowPaths);
        Assert.False(model.ShowImage);
        Assert.Contains(nameof(model.ShowPaths), changes);
        Assert.Contains(nameof(model.ShowImage), changes);
    }

    [Fact]
    public void PayloadChanges_UpdatePathAndImageSummaries()
    {
        var model = Model();

        model.Paths = ["C:\\first.txt", "C:\\second.txt"];
        model.ImagePng = new byte[128];

        Assert.True(model.HasPaths);
        Assert.True(model.HasImage);
        Assert.Contains("128", model.ImageSummary);
    }

    [Fact]
    public void Arguments_AreStructuredSortedAndRejectDuplicateKeys()
    {
        var model = Model();
        model.LoadArguments(new Dictionary<string, string>
        {
            ["z"] = "last",
            ["a"] = "first",
        });

        Assert.Equal(new[] { "a", "z" }, model.Arguments.Select(item => item.Key));
        model.Arguments[1].Key = "a";
        model.RefreshArgumentValidation();

        Assert.True(model.HasArgumentError);
        Assert.False(model.TryBuildArguments(out _));

        model.Arguments[1].Key = "z";
        Assert.True(model.TryBuildArguments(out var arguments));
        Assert.Equal("last", arguments["z"]);
    }

    [Fact]
    public void AddArgument_CreatesUniqueKeysAndHonorsMaximum()
    {
        var model = Model();

        for (var index = 0; index < 64; index++) Assert.True(model.AddArgument());

        Assert.False(model.AddArgument());
        Assert.False(model.CanAddArgument);
        Assert.Equal(64, model.Arguments.Select(item => item.Key).Distinct().Count());
    }

    [Fact]
    public void ApplyArgumentPreset_ReplacesArgumentsWithRegisteredDefensiveCopy()
    {
        var presetArguments = new Dictionary<string, string>
        {
            ["amount"] = "100",
            ["compact"] = "true",
        };
        var model = Model(
            [new WorkflowArgumentPresetOption("batch", "Batch", presetArguments)]);
        model.LoadArguments(new Dictionary<string, string> { ["old"] = "value" });
        model.SelectedArgumentPreset = new WorkflowArgumentPresetOption(
            "BATCH",
            "Forged",
            new Dictionary<string, string> { ["amount"] = "1" });

        var applied = model.ApplySelectedArgumentPreset();
        presetArguments["amount"] = "999";

        Assert.True(applied);
        Assert.DoesNotContain(model.Arguments, item => item.Key == "old");
        Assert.Equal("100", model.Arguments.Single(item => item.Key == "amount").Value);
        Assert.True(model.TryBuildArguments(out var arguments));
        Assert.Equal("true", arguments["compact"]);
    }

    [Fact]
    public void SchemaArguments_ExposeTypedEditorsDefaultsAndCanonicalValues()
    {
        var model = Model(schema:
        [
            Schema("name", PluginCommandArgumentType.String, defaultValue: "Long"),
            Schema("count", PluginCommandArgumentType.Integer, required: true),
            Schema("enabled", PluginCommandArgumentType.Boolean),
            Schema(
                "mode",
                PluginCommandArgumentType.Enum,
                enumValues: ["standard", "compact"]),
        ]);
        model.LoadArguments(new Dictionary<string, string>
        {
            ["count"] = "0010",
            ["enabled"] = "TRUE",
            ["mode"] = "compact",
        });

        Assert.True(model.UsesArgumentSchema);
        Assert.False(model.UsesAdvancedArguments);
        Assert.Equal(4, model.SchemaArguments.Count);
        Assert.True(model.SchemaArguments.Single(item => item.Key == "name").IsTextEditor);
        Assert.True(model.SchemaArguments.Single(item => item.Key == "enabled").IsBooleanEditor);
        Assert.True(model.SchemaArguments.Single(item => item.Key == "mode").IsEnumEditor);
        Assert.True(model.TryBuildArguments(out var arguments), model.ArgumentError);
        Assert.Equal("Long", arguments["name"]);
        Assert.Equal("10", arguments["count"]);
        Assert.Equal("true", arguments["enabled"]);
        Assert.Equal("compact", arguments["mode"]);
    }

    [Fact]
    public void SchemaArguments_ShowInlineErrorsWithoutLeakingSensitiveValues()
    {
        const string privateValue = "private-token";
        var model = Model(schema:
        [
            Schema(
                "token",
                PluginCommandArgumentType.String,
                required: true,
                sensitive: true,
                minLength: 20),
        ]);
        model.LoadArguments(new Dictionary<string, string> { ["token"] = privateValue });

        var item = Assert.Single(model.SchemaArguments);
        Assert.True(item.IsSensitiveEditor);
        Assert.True(item.HasError);
        Assert.True(model.HasArgumentError);
        Assert.DoesNotContain(privateValue, item.Error);
        Assert.DoesNotContain(privateValue, model.ArgumentError);
        Assert.DoesNotContain(privateValue, item.ConstraintSummary);
        Assert.False(model.TryBuildArguments(out _));
    }

    [Fact]
    public void SchemaArguments_BindingCanSatisfyRequiredParameter()
    {
        var model = Model(
            schema:
            [
                Schema("count", PluginCommandArgumentType.Integer, required: true),
            ],
            outputs:
            [
                new WorkflowBindingOutputOption(
                    "source",
                    "result",
                    PluginCommandOutputType.Text,
                    "Result"),
            ]);
        model.LoadArguments(new Dictionary<string, string>());
        model.BindingEditor.LoadBindings(
        [
            new WorkflowValueBinding(
                "source",
                "result",
                WorkflowBindingTarget.Argument,
                "count"),
        ]);
        model.RefreshArgumentValidation();

        Assert.False(model.HasArgumentError);
        Assert.True(model.TryBuildArguments(out var arguments), model.ArgumentError);
        Assert.Empty(arguments);
    }

    [Fact]
    public void SchemaArguments_PreserveUnknownValuesUntilExplicitlyRemoved()
    {
        var model = Model(schema:
        [
            Schema("known", PluginCommandArgumentType.String),
        ]);
        model.LoadArguments(new Dictionary<string, string>
        {
            ["known"] = "visible",
            ["removed-by-upgrade"] = "private-value",
        });

        Assert.True(model.HasUnrecognizedArguments);
        Assert.True(model.HasArgumentError);
        var unknown = Assert.Single(model.Arguments);
        Assert.Equal("removed-by-upgrade", unknown.Key);
        Assert.DoesNotContain("private-value", model.ArgumentError);
        Assert.False(model.TryBuildArguments(out _));

        Assert.True(model.RemoveArgument(unknown));
        Assert.False(model.HasUnrecognizedArguments);
        Assert.True(model.TryBuildArguments(out var arguments), model.ArgumentError);
        Assert.Equal("visible", arguments["known"]);
    }

    private static WorkflowInvocationEditorModel Model(
        IReadOnlyList<WorkflowArgumentPresetOption>? presets = null,
        IReadOnlyList<PluginCommandArgumentDeclaration>? schema = null,
        IReadOnlyList<WorkflowBindingOutputOption>? outputs = null)
        => new()
        {
            StepId = "step-1",
            Role = WorkflowCommandRole.Primary,
            RoleLabel = "命令输入",
            InputOptions =
            [
                new WorkflowInputTypeOption(AcceptedInputType.None, "无输入"),
                new WorkflowInputTypeOption(AcceptedInputType.Files, "多个文件"),
            ],
            ArgumentPresets = presets ?? Array.Empty<WorkflowArgumentPresetOption>(),
            ArgumentSchema = schema ?? Array.Empty<PluginCommandArgumentDeclaration>(),
            BindingEditor = new WorkflowBindingEditorModel(
                outputs ?? Array.Empty<WorkflowBindingOutputOption>(),
                AcceptedInputType.None),
        };

    private static PluginCommandArgumentDeclaration Schema(
        string key,
        PluginCommandArgumentType type,
        bool required = false,
        string? defaultValue = null,
        bool sensitive = false,
        int? minLength = null,
        IReadOnlyList<string>? enumValues = null)
        => new()
        {
            Key = key,
            Name = key,
            Description = key + " description",
            Type = type,
            Required = required,
            DefaultValue = defaultValue,
            Sensitive = sensitive,
            MinLength = minLength,
            EnumValues = enumValues?.ToList() ?? new List<string>(),
        };
}
