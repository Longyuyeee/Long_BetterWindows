using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class CommandWorkflowPlannerTests
{
    [Fact]
    public void Preflight_ValidCompensatingWorkflow_ProducesPermissionPlanAndStableFingerprint()
    {
        var registry = CreateRegistry();
        var planner = new CommandWorkflowPlanner(registry);
        var first = Workflow(new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });
        var second = Workflow(new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" });

        var result = planner.Preflight(first);
        var reorderedResult = planner.Preflight(second);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal(64, result.Fingerprint.Length);
        Assert.Equal(result.Fingerprint, reorderedResult.Fingerprint);
        var permission = Assert.Single(result.Permissions);
        Assert.Equal("files", permission.PluginId);
        Assert.Equal("1.0.0", permission.PluginVersion);
        Assert.Equal(new[] { "clipboard", "file_ops" }, permission.Capabilities);
    }

    [Fact]
    public void Preflight_MutatingStepWithoutCompensation_IsRejectedBeforeExecution()
    {
        var planner = new CommandWorkflowPlanner(CreateRegistry());
        var workflow = Workflow() with
        {
            Steps =
            [
                new CommandWorkflowStep(
                    "rename",
                    WorkflowStepEffect.Mutating,
                    Command("files:rename", "rename")),
            ],
        };

        var result = planner.Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("requires compensation"));
    }

    [Fact]
    public void Preflight_MissingCommandAndDuplicateStepIds_AreRejected()
    {
        var planner = new CommandWorkflowPlanner(CreateRegistry());
        var workflow = Workflow() with
        {
            FailureMode = WorkflowFailureMode.Stop,
            Steps =
            [
                new CommandWorkflowStep("same", WorkflowStepEffect.ReadOnly, Command("missing:open", "open")),
                new CommandWorkflowStep("SAME", WorkflowStepEffect.ReadOnly, Command("files:inspect", "inspect")),
            ],
        };

        var result = planner.Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("was not found"));
        Assert.Contains(result.Issues, issue => issue.Contains("duplicated"));
    }

    [Fact]
    public void Preflight_IncompatibleInput_IsRejected()
    {
        var planner = new CommandWorkflowPlanner(CreateRegistry());
        var workflow = Workflow() with
        {
            FailureMode = WorkflowFailureMode.Stop,
            Steps =
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "files:inspect",
                        new PluginCommandInvocation
                        {
                            CommandId = "inspect",
                            InputType = AcceptedInputType.Url,
                        })),
            ],
        };

        var result = planner.Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("input type"));
    }

    [Fact]
    public void Preflight_NoneInputCannotBypassCommandContract()
    {
        var planner = new CommandWorkflowPlanner(CreateRegistry());
        var workflow = Workflow() with
        {
            FailureMode = WorkflowFailureMode.Stop,
            Steps =
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "files:inspect",
                        new PluginCommandInvocation { CommandId = "inspect" })),
            ],
        };

        var result = planner.Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("input type"));
    }

    [Fact]
    public void Preflight_ChangedInvocationContent_ChangesFingerprintWithoutExposingContent()
    {
        var planner = new CommandWorkflowPlanner(CreateRegistry());
        var original = Workflow() with
        {
            Steps =
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    Command("files:inspect", "inspect", "private-a")),
            ],
        };
        var changed = original with
        {
            Steps =
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    Command("files:inspect", "inspect", "private-b")),
            ],
        };

        var first = planner.Preflight(original);
        var second = planner.Preflight(changed);

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.DoesNotContain("private-a", first.Fingerprint);
    }

    [Fact]
    public void Preflight_ChangedPluginVersion_InvalidatesPermissionReviewFingerprint()
    {
        var original = new CommandWorkflowPlanner(CreateRegistry("1.0.0")).Preflight(Workflow());
        var upgraded = new CommandWorkflowPlanner(CreateRegistry("1.1.0")).Preflight(Workflow());

        Assert.True(original.IsValid);
        Assert.True(upgraded.IsValid);
        Assert.NotEqual(original.Fingerprint, upgraded.Fingerprint);
    }

    private static CommandWorkflowDefinition Workflow(
        IReadOnlyDictionary<string, string>? arguments = null)
        => new(
            "workflow.files.safe-rename",
            "Safe rename",
            WorkflowFailureMode.Compensate,
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    Command("files:inspect", "inspect")),
                new CommandWorkflowStep(
                    "rename",
                    WorkflowStepEffect.Mutating,
                    new WorkflowCommand(
                        "files:rename",
                        new PluginCommandInvocation
                        {
                            CommandId = "rename",
                            InputType = AcceptedInputType.File,
                            Text = "new-name",
                            Arguments = arguments ?? new Dictionary<string, string>(),
                        }),
                    Command("files:rename", "rename", "old-name")),
            ]);

    private static WorkflowCommand Command(
        string key,
        string commandId,
        string? text = null)
        => new(
            key,
            new PluginCommandInvocation
            {
                CommandId = commandId,
                InputType = AcceptedInputType.File,
                Text = text,
            });

    private static PluginRegistry CreateRegistry(string version = "1.0.0")
    {
        var registry = new PluginRegistry();
        registry.Register(
            new PluginManifest
            {
                Id = "files",
                Name = "Files",
                Version = version,
                EntryPoint = "files.dll",
                Capabilities = ["file_ops", "clipboard"],
                Commands =
                [
                    new PluginCommand
                    {
                        Id = "inspect",
                        Title = "Inspect",
                        AcceptedInputs = [AcceptedInputType.File],
                    },
                    new PluginCommand
                    {
                        Id = "rename",
                        Title = "Rename",
                        AcceptedInputs = [AcceptedInputType.File],
                    },
                ],
            },
            new TestPlugin(),
            null,
            "/files");
        return registry;
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "files";
        public string Name => "Files";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
