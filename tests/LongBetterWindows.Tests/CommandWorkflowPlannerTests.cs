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

    [Fact]
    public void Preflight_ValidPriorStepBindingChangesFingerprint()
    {
        var registry = CreateRegistry();
        var original = Workflow();
        var bound = original with
        {
            Steps =
            [
                original.Steps[0],
                original.Steps[1] with
                {
                    Command = original.Steps[1].Command! with
                    {
                        Bindings =
                        [
                            new WorkflowValueBinding(
                                "inspect",
                                "selected-path",
                                WorkflowBindingTarget.Path),
                        ],
                    },
                },
            ],
        };

        var unboundResult = new CommandWorkflowPlanner(registry).Preflight(original);
        var boundResult = new CommandWorkflowPlanner(registry).Preflight(bound);

        Assert.True(boundResult.IsValid, string.Join(Environment.NewLine, boundResult.Issues));
        Assert.NotEqual(unboundResult.Fingerprint, boundResult.Fingerprint);
    }

    [Fact]
    public void Preflight_FutureStepAndDuplicateTargetBindingsAreRejected()
    {
        var registry = CreateRegistry();
        var original = Workflow();
        var workflow = original with
        {
            Steps =
            [
                original.Steps[0] with
                {
                    Command = original.Steps[0].Command! with
                    {
                        Bindings =
                        [
                            new WorkflowValueBinding("rename", "one", WorkflowBindingTarget.Text),
                            new WorkflowValueBinding("rename", "two", WorkflowBindingTarget.Text),
                        ],
                    },
                },
                original.Steps[1],
            ],
        };

        var result = new CommandWorkflowPlanner(registry).Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("already be available"));
        Assert.Contains(result.Issues, issue => issue.Contains("duplicate text bindings"));
    }

    [Fact]
    public void Preflight_UndeclaredAndMismatchedOutputBindingsAreRejected()
    {
        var registry = CreateRegistry();
        var original = Workflow();
        var workflow = original with
        {
            Steps =
            [
                original.Steps[0],
                original.Steps[1] with
                {
                    Command = original.Steps[1].Command! with
                    {
                        Bindings =
                        [
                            new WorkflowValueBinding(
                                "inspect",
                                "missing",
                                WorkflowBindingTarget.Path),
                            new WorkflowValueBinding(
                                "inspect",
                                "selected-path",
                                WorkflowBindingTarget.Text),
                        ],
                    },
                },
            ],
        };

        var result = new CommandWorkflowPlanner(registry).Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("not declared"));
        Assert.Contains(result.Issues, issue => issue.Contains("output type is incompatible"));
    }

    [Fact]
    public void Preflight_ValidatesPrimaryAndCompensationLiteralArguments()
    {
        var registry = CreateRegistry();
        registry.Commands.Get("files:rename")!.Command.ArgumentSchema.Add(
            Argument("count", required: true, maximum: 100));
        var workflow = Workflow(
            new Dictionary<string, string> { ["count"] = "not-an-integer" });

        var result = new CommandWorkflowPlanner(registry).Preflight(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Contains("command parameters", StringComparison.Ordinal)
            && issue.Contains("count", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue =>
            issue.Contains("compensation parameters", StringComparison.Ordinal)
            && issue.Contains("count", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Contains("not-an-integer", StringComparison.Ordinal));
    }

    [Fact]
    public void Preflight_ArgumentBindingMustTargetSchemaAndCanSatisfyRequiredValue()
    {
        var registry = CreateRegistry();
        registry.Commands.Get("files:inspect")!.Command.Outputs.Add(
            new PluginCommandOutputDeclaration
            {
                Key = "count-text",
                Type = PluginCommandOutputType.Text,
            });
        registry.Commands.Get("files:rename")!.Command.ArgumentSchema.Add(
            Argument("count", required: true, maximum: 100));
        var valid = new CommandWorkflowDefinition(
            "workflow.bound-argument",
            "Bound argument",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "inspect",
                    WorkflowStepEffect.ReadOnly,
                    Command("files:inspect", "inspect")),
                new CommandWorkflowStep(
                    "rename",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "files:rename",
                        new PluginCommandInvocation
                        {
                            CommandId = "rename",
                            InputType = AcceptedInputType.File,
                        },
                        [
                            new WorkflowValueBinding(
                                "inspect",
                                "count-text",
                                WorkflowBindingTarget.Argument,
                                "count"),
                        ])),
            ]);
        var invalid = valid with
        {
            Steps =
            [
                valid.Steps[0],
                valid.Steps[1] with
                {
                    Command = valid.Steps[1].Command! with
                    {
                        Bindings =
                        [
                            new WorkflowValueBinding(
                                "inspect",
                                "count-text",
                                WorkflowBindingTarget.Argument,
                                "missing"),
                        ],
                    },
                },
            ],
        };

        var validResult = new CommandWorkflowPlanner(registry).Preflight(valid);
        var invalidResult = new CommandWorkflowPlanner(registry).Preflight(invalid);

        Assert.True(validResult.IsValid, string.Join(Environment.NewLine, validResult.Issues));
        Assert.False(invalidResult.IsValid);
        Assert.Contains(invalidResult.Issues, issue =>
            issue.Contains("target is not declared", StringComparison.Ordinal));
    }

    [Fact]
    public void Preflight_SchemaConstraintChangeInvalidatesFingerprint()
    {
        var originalRegistry = CreateRegistry();
        var changedRegistry = CreateRegistry();
        originalRegistry.Commands.Get("files:rename")!.Command.ArgumentSchema.Add(
            Argument("count", defaultValue: "10", maximum: 100));
        changedRegistry.Commands.Get("files:rename")!.Command.ArgumentSchema.Add(
            Argument("count", defaultValue: "10", maximum: 200));

        var original = new CommandWorkflowPlanner(originalRegistry).Preflight(Workflow());
        var changed = new CommandWorkflowPlanner(changedRegistry).Preflight(Workflow());

        Assert.True(original.IsValid, string.Join(Environment.NewLine, original.Issues));
        Assert.True(changed.IsValid, string.Join(Environment.NewLine, changed.Issues));
        Assert.NotEqual(original.Fingerprint, changed.Fingerprint);
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

    private static PluginCommandArgumentDeclaration Argument(
        string key,
        bool required = false,
        string? defaultValue = null,
        decimal? maximum = null)
        => new()
        {
            Key = key,
            Name = key,
            Type = PluginCommandArgumentType.Integer,
            Required = required,
            DefaultValue = defaultValue,
            Minimum = 1,
            Maximum = maximum,
        };

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
                        Outputs =
                        [
                            new PluginCommandOutputDeclaration
                            {
                                Key = "selected-path",
                                Type = PluginCommandOutputType.Path,
                            },
                        ],
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
