using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class CommandWorkflowBindingResolverTests
{
    [Fact]
    public void TrySnapshotOutputs_RejectsInvalidKeysAndPathValues()
    {
        var invalidKey = PluginCommandResult.Success(outputs: new Dictionary<string, PluginCommandOutput>
        {
            ["not valid"] = new(PluginCommandOutputType.Text, "value"),
        });
        var emptyPath = PluginCommandResult.Success(outputs: new Dictionary<string, PluginCommandOutput>
        {
            ["path"] = new(PluginCommandOutputType.Path, " "),
        });

        Assert.False(CommandWorkflowBindingResolver.TrySnapshotOutputs(
            invalidKey, out _, out var keyError));
        Assert.False(CommandWorkflowBindingResolver.TrySnapshotOutputs(
            emptyPath, out _, out var pathError));
        Assert.Contains("key", keyError);
        Assert.Contains("value", pathError);
    }

    [Fact]
    public void TrySnapshotOutputs_EnforcesCountAndValueLimits()
    {
        var tooMany = Enumerable.Range(0, CommandWorkflowBindingResolver.MaximumOutputCount + 1)
            .ToDictionary(
                index => $"output-{index}",
                index => new PluginCommandOutput(PluginCommandOutputType.Text, index.ToString()));
        var tooLarge = new Dictionary<string, PluginCommandOutput>
        {
            ["text"] = new(
                PluginCommandOutputType.Text,
                new string('x', CommandWorkflowBindingResolver.MaximumOutputValueLength + 1)),
        };

        Assert.False(CommandWorkflowBindingResolver.TrySnapshotOutputs(
            PluginCommandResult.Success(outputs: tooMany), out _, out _));
        Assert.False(CommandWorkflowBindingResolver.TrySnapshotOutputs(
            PluginCommandResult.Success(outputs: tooLarge), out _, out _));
    }

    [Fact]
    public void Resolve_AppliesTypedBindingsWithoutMutatingStoredInvocation()
    {
        var invocation = new PluginCommandInvocation
        {
            CommandId = "target",
            InputType = AcceptedInputType.Files,
            Text = "literal",
            Paths = ["C:\\literal.txt"],
            Arguments = new Dictionary<string, string> { ["mode"] = "literal" },
        };
        var command = new WorkflowCommand(
            "plugin:target",
            invocation,
            [
                new WorkflowValueBinding("source", "text", WorkflowBindingTarget.Text),
                new WorkflowValueBinding("source", "path", WorkflowBindingTarget.Path),
                new WorkflowValueBinding("source", "mode", WorkflowBindingTarget.Argument, "mode"),
            ]);
        var outputs = new Dictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>>
        {
            ["source"] = new Dictionary<string, PluginCommandOutput>
            {
                ["text"] = new(PluginCommandOutputType.Text, "resolved"),
                ["path"] = new(PluginCommandOutputType.Path, "C:\\resolved.txt"),
                ["mode"] = new(PluginCommandOutputType.Text, "dynamic"),
            },
        };

        var result = CommandWorkflowBindingResolver.Resolve(command, outputs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("resolved", result.Invocation!.Text);
        Assert.Equal(new[] { "C:\\literal.txt", "C:\\resolved.txt" }, result.Invocation.Paths);
        Assert.Equal("dynamic", result.Invocation.Arguments["mode"]);
        Assert.Equal("literal", invocation.Text);
        Assert.Single(invocation.Paths);
        Assert.Equal("literal", invocation.Arguments["mode"]);
    }

    [Fact]
    public void Resolve_RejectsMissingAndMismatchedOutputsWithoutExposingValues()
    {
        var command = new WorkflowCommand(
            "plugin:target",
            new PluginCommandInvocation
            {
                CommandId = "target",
                InputType = AcceptedInputType.File,
            },
            [new WorkflowValueBinding("source", "path", WorkflowBindingTarget.Path)]);
        var mismatched = new Dictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>>
        {
            ["source"] = new Dictionary<string, PluginCommandOutput>
            {
                ["path"] = new(PluginCommandOutputType.Text, "private-value"),
            },
        };

        var missing = CommandWorkflowBindingResolver.Resolve(
            command,
            new Dictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>>());
        var mismatch = CommandWorkflowBindingResolver.Resolve(command, mismatched);

        Assert.False(missing.IsSuccess);
        Assert.False(mismatch.IsSuccess);
        Assert.DoesNotContain("private-value", mismatch.Error);
    }

    [Fact]
    public void TrySnapshotDeclaredOutputs_RejectsUndeclaredAndWrongTypes()
    {
        var declarations = new[]
        {
            new PluginCommandOutputDeclaration
            {
                Key = "path",
                Type = PluginCommandOutputType.Path,
            },
        };
        var undeclared = PluginCommandResult.Success(outputs: new Dictionary<string, PluginCommandOutput>
        {
            ["other"] = new(PluginCommandOutputType.Path, "C:\\private.txt"),
        });
        var wrongType = PluginCommandResult.Success(outputs: new Dictionary<string, PluginCommandOutput>
        {
            ["path"] = new(PluginCommandOutputType.Text, "private"),
        });

        Assert.False(CommandWorkflowBindingResolver.TrySnapshotDeclaredOutputs(
            undeclared, declarations, out _, out var undeclaredError));
        Assert.False(CommandWorkflowBindingResolver.TrySnapshotDeclaredOutputs(
            wrongType, declarations, out _, out var typeError));
        Assert.Contains("undeclared", undeclaredError);
        Assert.Contains("wrong declared type", typeError);
    }
}
