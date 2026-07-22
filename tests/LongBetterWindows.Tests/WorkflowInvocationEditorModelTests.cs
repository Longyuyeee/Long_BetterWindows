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

    private static WorkflowInvocationEditorModel Model()
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
            Arguments = new Dictionary<string, string>(),
        };
}
