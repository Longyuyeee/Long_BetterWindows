using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Views;

namespace LongBetterWindows.Tests;

public sealed class WorkflowBindingEditorModelTests
{
    [Fact]
    public void OutputType_RestrictsCompatibleTargets()
    {
        var model = Model(AcceptedInputType.Files);
        Assert.True(model.AddBinding());

        var binding = Assert.Single(model.Bindings);
        Assert.Equal(PluginCommandOutputType.Text, binding.Output.Type);
        Assert.Equal(
            new[] { WorkflowBindingTarget.Text, WorkflowBindingTarget.Argument },
            binding.TargetOptions.Select(option => option.Value));

        binding.Output = binding.OutputOptions.Single(option => option.OutputKey == "folder");
        Assert.Equal(WorkflowBindingTarget.Path, binding.Target);
        Assert.Equal(
            WorkflowBindingTarget.Path,
            Assert.Single(binding.TargetOptions).Value);
    }

    [Fact]
    public void InputTypeChange_MarksPathBindingIncompatible()
    {
        var model = Model(AcceptedInputType.Folder);
        model.LoadBindings(
        [
            new WorkflowValueBinding("step-1", "folder", WorkflowBindingTarget.Path),
        ]);

        model.SetInputType(AcceptedInputType.None);

        Assert.True(model.HasError);
        Assert.False(model.TryBuildBindings(out _));
    }

    [Fact]
    public void MissingDeclaration_IsPreservedForExplicitRepair()
    {
        var model = Model(AcceptedInputType.Text);
        model.LoadBindings(
        [
            new WorkflowValueBinding(
                "removed-step",
                "removed-output",
                WorkflowBindingTarget.Argument,
                "value"),
        ]);

        var binding = Assert.Single(model.Bindings);
        Assert.False(binding.Output.IsAvailable);
        Assert.Equal("removed-output", binding.Output.OutputKey);
        Assert.True(model.HasError);
    }

    [Fact]
    public void ArgumentTargets_RequireUniqueKeys()
    {
        var model = Model(AcceptedInputType.None);
        Assert.True(model.AddBinding());
        Assert.True(model.AddBinding());
        model.Bindings[0].ArgumentKey = "same";
        model.Bindings[1].ArgumentKey = "same";

        Assert.True(model.HasError);
        Assert.False(model.TryBuildBindings(out _));

        model.Bindings[1].ArgumentKey = "other";
        Assert.True(model.TryBuildBindings(out var bindings));
        Assert.Equal(2, bindings.Count);
    }

    [Fact]
    public void SecondTextBinding_DefaultsToUniqueArgumentTarget()
    {
        var model = Model(AcceptedInputType.Text);

        Assert.True(model.AddBinding());
        Assert.True(model.AddBinding());

        Assert.Equal(WorkflowBindingTarget.Text, model.Bindings[0].Target);
        Assert.Equal(WorkflowBindingTarget.Argument, model.Bindings[1].Target);
        Assert.False(model.HasError);
    }

    [Fact]
    public void DeclaredArgumentTargets_UseAvailableSchemaKeysAndRejectUnknownValues()
    {
        var model = Model(AcceptedInputType.None, ["count", "mode"]);

        Assert.True(model.AddBinding());
        Assert.True(model.AddBinding());

        Assert.Equal("count", model.Bindings[0].ArgumentKey);
        Assert.Equal("mode", model.Bindings[1].ArgumentKey);
        Assert.True(model.Bindings[0].ShowArgumentKeyOptions);
        Assert.False(model.Bindings[0].ShowArgumentKeyTextBox);
        Assert.False(model.HasError);
        Assert.False(model.CanAdd);

        model.Bindings[1].ArgumentKey = "unknown";
        Assert.True(model.HasError);
        Assert.Contains("Schema", model.Error);
    }

    private static WorkflowBindingEditorModel Model(
        AcceptedInputType inputType,
        IReadOnlyList<string>? declaredArgumentKeys = null)
        => new(
        [
            new WorkflowBindingOutputOption(
                "step-1",
                "title",
                PluginCommandOutputType.Text,
                "Generated title"),
            new WorkflowBindingOutputOption(
                "step-1",
                "folder",
                PluginCommandOutputType.Path,
                "Selected folder"),
        ],
        inputType,
        declaredArgumentKeys);
}
