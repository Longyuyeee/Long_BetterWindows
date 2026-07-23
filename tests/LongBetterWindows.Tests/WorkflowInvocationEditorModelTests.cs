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

    private static WorkflowInvocationEditorModel Model(
        IReadOnlyList<WorkflowArgumentPresetOption>? presets = null)
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
        };
}
