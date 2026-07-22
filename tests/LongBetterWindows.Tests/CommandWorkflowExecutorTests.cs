using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class CommandWorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_MissingAuthorization_RejectsWithoutRunningCommands()
    {
        var registry = CreateRegistry();
        var runner = new FakeRunner((_, _) => PluginCommandResult.Success());
        var executor = new CommandWorkflowExecutor(registry, runner);

        var result = await executor.ExecuteAsync(Workflow(), authorization: null);

        Assert.Equal(WorkflowExecutionStatus.Rejected, result.Status);
        Assert.Empty(runner.Calls);
        Assert.Contains(result.Events, item => item.Kind == WorkflowExecutionEventKind.WorkflowRejected);
    }

    [Fact]
    public async Task ExecuteAsync_ChangedPermissionPlan_RejectsStaleApproval()
    {
        var registry = CreateRegistry();
        var workflow = Workflow();
        var preflight = new CommandWorkflowPlanner(registry).Preflight(workflow);
        var stale = new CommandWorkflowAuthorization(
            preflight.Fingerprint,
            [new WorkflowPermissionRequirement("workflow", "1.0.0", 1, [])]);
        var runner = new FakeRunner((_, _) => PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, stale);

        Assert.Equal(WorkflowExecutionStatus.Rejected, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ValidAuthorization_RunsStepsInOrderAndRecordsTimeline()
    {
        var registry = CreateRegistry();
        var workflow = Workflow(includeFailure: false);
        var runner = new FakeRunner((_, _) => PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);
        Assert.Equal(new[] { "workflow:write-one", "workflow:write-two" }, runner.Calls);
        Assert.Equal(
            Enumerable.Range(1, result.Events.Count),
            result.Events.Select(item => item.Sequence));
        Assert.Equal(2, result.Events.Count(item => item.Kind == WorkflowExecutionEventKind.StepSucceeded));
        Assert.Equal(WorkflowExecutionEventKind.WorkflowCompleted, result.Events[^1].Kind);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardFailure_CompensatesCompletedMutationsInReverseOrder()
    {
        var registry = CreateRegistry();
        var workflow = Workflow();
        var runner = new FakeRunner((command, _) => command == "workflow:fail"
            ? PluginCommandResult.Failure("expected failure")
            : PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Compensated, result.Status);
        Assert.Equal(
            new[]
            {
                "workflow:write-one",
                "workflow:write-two",
                "workflow:fail",
                "workflow:undo-two",
                "workflow:undo-one",
            },
            runner.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_CompensationFailure_ContinuesRemainingCompensations()
    {
        var registry = CreateRegistry();
        var workflow = Workflow();
        var runner = new FakeRunner((command, _) => command is "workflow:fail" or "workflow:undo-two"
            ? PluginCommandResult.Failure("expected failure")
            : PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.CompensationFailed, result.Status);
        Assert.Equal("workflow:undo-one", runner.Calls[^1]);
        Assert.Contains(result.Events, item => item.Kind == WorkflowExecutionEventKind.CompensationFailed);
        Assert.Contains(result.Events, item => item.Kind == WorkflowExecutionEventKind.CompensationSucceeded);
    }

    [Fact]
    public async Task ExecuteAsync_StopMode_DoesNotRunCompensation()
    {
        var registry = CreateRegistry();
        var workflow = Workflow() with { FailureMode = WorkflowFailureMode.Stop };
        var runner = new FakeRunner((command, _) => command == "workflow:fail"
            ? PluginCommandResult.Failure("expected failure")
            : PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.DoesNotContain(runner.Calls, command => command.Contains("undo"));
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_UsesIndependentTokenForCompensation()
    {
        var registry = CreateRegistry();
        var workflow = Workflow(includeFailure: false);
        using var cancellation = new CancellationTokenSource();
        var compensationSawCancelledToken = true;
        var runner = new FakeRunner((command, token) =>
        {
            if (command == "workflow:write-one") cancellation.Cancel();
            if (command == "workflow:undo-one") compensationSawCancelledToken = token.IsCancellationRequested;
            return PluginCommandResult.Success();
        });

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow), cancellation.Token);

        Assert.Equal(WorkflowExecutionStatus.Compensated, result.Status);
        Assert.Equal(new[] { "workflow:write-one", "workflow:undo-one" }, runner.Calls);
        Assert.False(compensationSawCancelledToken);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedPluginChangesMidRun_BlocksUntrustedCompensation()
    {
        var registry = CreateRegistry();
        var workflow = Workflow(includeFailure: false);
        var runner = new FakeRunner((command, _) =>
        {
            if (command == "workflow:write-one")
            {
                registry.Unregister("workflow");
                RegisterWorkflowPlugin(registry);
            }
            return PluginCommandResult.Success();
        });

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.CompensationFailed, result.Status);
        Assert.Equal(new[] { "workflow:write-one" }, runner.Calls);
        Assert.Contains(
            result.Events,
            item => item.Message?.Contains("Compensation was blocked") == true);
    }

    [Fact]
    public async Task ExecuteAsync_UnrelatedPluginRegistration_DoesNotInvalidateApproval()
    {
        var registry = CreateRegistry();
        var workflow = Workflow(includeFailure: false);
        var runner = new FakeRunner((command, _) =>
        {
            if (command == "workflow:write-one") RegisterExtraPlugin(registry);
            return PluginCommandResult.Success();
        });

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);
        Assert.Equal(new[] { "workflow:write-one", "workflow:write-two" }, runner.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesPriorStepOutputIntoTargetInvocation()
    {
        var registry = CreateRegistry();
        var workflow = new CommandWorkflowDefinition(
            "workflow.binding",
            "Binding workflow",
            WorkflowFailureMode.Stop,
            [
                new CommandWorkflowStep(
                    "source",
                    WorkflowStepEffect.ReadOnly,
                    Command("write-one")),
                new CommandWorkflowStep(
                    "target",
                    WorkflowStepEffect.ReadOnly,
                    new WorkflowCommand(
                        "workflow:write-two",
                        new PluginCommandInvocation
                        {
                            CommandId = "write-two",
                            InputType = AcceptedInputType.File,
                        },
                        [new WorkflowValueBinding(
                            "source",
                            "selected-path",
                            WorkflowBindingTarget.Path)])),
            ]);
        var runner = new CapturingRunner((command, _) => command == "workflow:write-one"
            ? PluginCommandResult.Success(outputs: new Dictionary<string, PluginCommandOutput>
            {
                ["selected-path"] = new(PluginCommandOutputType.Path, "C:\\private.txt"),
            })
            : PluginCommandResult.Success());

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Completed, result.Status);
        Assert.Equal("C:\\private.txt", Assert.Single(runner.Invocations[1]!.Paths));
        Assert.DoesNotContain(result.Events, item => item.Message?.Contains("private.txt") == true);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOutputsFailStepWithoutLeakingValues()
    {
        var registry = CreateRegistry();
        var workflow = new CommandWorkflowDefinition(
            "workflow.invalid-output",
            "Invalid output",
            WorkflowFailureMode.Stop,
            [new CommandWorkflowStep(
                "source",
                WorkflowStepEffect.ReadOnly,
                Command("write-one"))]);
        var privateValue = new string('p', CommandWorkflowBindingResolver.MaximumOutputValueLength + 1);
        var runner = new CapturingRunner((_, _) => PluginCommandResult.Success(
            outputs: new Dictionary<string, PluginCommandOutput>
            {
                ["secret"] = new(PluginCommandOutputType.Text, privateValue),
            }));

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Failed, result.Status);
        Assert.DoesNotContain(result.Events, item => item.Message?.Contains(privateValue) == true);
    }

    [Fact]
    public async Task ExecuteAsync_CompensationCanBindItsOwnPrimaryOutput()
    {
        var registry = CreateRegistry();
        var workflow = new CommandWorkflowDefinition(
            "workflow.compensation-binding",
            "Compensation binding",
            WorkflowFailureMode.Compensate,
            [
                new CommandWorkflowStep(
                    "write",
                    WorkflowStepEffect.Mutating,
                    Command("write-one"),
                    new WorkflowCommand(
                        "workflow:undo-one",
                        new PluginCommandInvocation { CommandId = "undo-one" },
                        [new WorkflowValueBinding(
                            "write",
                            "restore-token",
                            WorkflowBindingTarget.Argument,
                            "token")])),
                new CommandWorkflowStep(
                    "failure",
                    WorkflowStepEffect.ReadOnly,
                    Command("fail")),
            ]);
        var runner = new CapturingRunner((command, _) => command switch
        {
            "workflow:write-one" => PluginCommandResult.Success(
                outputs: new Dictionary<string, PluginCommandOutput>
                {
                    ["restore-token"] = new(PluginCommandOutputType.Text, "private-token"),
                }),
            "workflow:fail" => PluginCommandResult.Failure("expected failure"),
            _ => PluginCommandResult.Success(),
        });

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.Compensated, result.Status);
        var undoIndex = runner.Calls.IndexOf("workflow:undo-one");
        Assert.Equal("private-token", runner.Invocations[undoIndex]!.Arguments["token"]);
        Assert.DoesNotContain(result.Events, item => item.Message?.Contains("private-token") == true);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCompensationOutputsMarkCompensationFailed()
    {
        var registry = CreateRegistry();
        var workflow = new CommandWorkflowDefinition(
            "workflow.invalid-compensation-output",
            "Invalid compensation output",
            WorkflowFailureMode.Compensate,
            [
                new CommandWorkflowStep(
                    "write",
                    WorkflowStepEffect.Mutating,
                    Command("write-one"),
                    Command("undo-one")),
                new CommandWorkflowStep(
                    "failure",
                    WorkflowStepEffect.ReadOnly,
                    Command("fail")),
            ]);
        var runner = new CapturingRunner((command, _) => command switch
        {
            "workflow:fail" => PluginCommandResult.Failure("expected failure"),
            "workflow:undo-one" => PluginCommandResult.Success(
                outputs: new Dictionary<string, PluginCommandOutput>
                {
                    ["oversized"] = new(
                        PluginCommandOutputType.Text,
                        new string('x', CommandWorkflowBindingResolver.MaximumOutputValueLength + 1)),
                }),
            _ => PluginCommandResult.Success(),
        });

        var result = await new CommandWorkflowExecutor(registry, runner)
            .ExecuteAsync(workflow, Authorize(registry, workflow));

        Assert.Equal(WorkflowExecutionStatus.CompensationFailed, result.Status);
        Assert.Contains(result.Events, item => item.Kind == WorkflowExecutionEventKind.CompensationFailed);
    }

    private static CommandWorkflowAuthorization Authorize(
        PluginRegistry registry,
        CommandWorkflowDefinition workflow)
    {
        var preflight = new CommandWorkflowPlanner(registry).Preflight(workflow);
        Assert.True(preflight.IsValid, string.Join(Environment.NewLine, preflight.Issues));
        return new CommandWorkflowAuthorization(preflight.Fingerprint, preflight.Permissions);
    }

    private static CommandWorkflowDefinition Workflow(bool includeFailure = true)
    {
        var steps = new List<CommandWorkflowStep>
        {
            Mutating("one"),
            Mutating("two"),
        };
        if (includeFailure)
        {
            steps.Add(new CommandWorkflowStep(
                "fail",
                WorkflowStepEffect.ReadOnly,
                Command("fail")));
        }
        return new CommandWorkflowDefinition(
            "workflow.test",
            "Workflow test",
            WorkflowFailureMode.Compensate,
            steps);
    }

    private static CommandWorkflowStep Mutating(string suffix)
        => new(
            "write-" + suffix,
            WorkflowStepEffect.Mutating,
            Command("write-" + suffix),
            Command("undo-" + suffix));

    private static WorkflowCommand Command(string id)
        => new(
            "workflow:" + id,
            new PluginCommandInvocation { CommandId = id });

    private static PluginRegistry CreateRegistry()
    {
        var registry = new PluginRegistry();
        RegisterWorkflowPlugin(registry);
        return registry;
    }

    private static void RegisterWorkflowPlugin(PluginRegistry registry)
    {
        registry.Register(
            new PluginManifest
            {
                Id = "workflow",
                Name = "Workflow",
                Version = "1.0.0",
                EntryPoint = "workflow.dll",
                Capabilities = ["file_ops"],
                Commands =
                [
                    CommandManifest("write-one"),
                    CommandManifest("write-two"),
                    CommandManifest("undo-one"),
                    CommandManifest("undo-two"),
                    CommandManifest("fail"),
                ],
            },
            new TestPlugin(),
            null,
            "/workflow");
    }

    private static void RegisterExtraPlugin(PluginRegistry registry)
    {
        registry.Register(
            new PluginManifest
            {
                Id = "extra",
                Name = "Extra",
                Version = "1.0.0",
                EntryPoint = "extra.dll",
            },
            new ExtraPlugin(),
            null,
            "/extra");
    }

    private static PluginCommand CommandManifest(string id)
        => new()
        {
            Id = id,
            Title = id,
            AcceptedInputs =
            [
                AcceptedInputType.None,
                AcceptedInputType.File,
                AcceptedInputType.Files,
            ],
        };

    private sealed class FakeRunner : IWorkflowCommandRunner
    {
        private readonly Func<string, CancellationToken, PluginCommandResult> _execute;

        public FakeRunner(Func<string, CancellationToken, PluginCommandResult> execute)
        {
            _execute = execute;
        }

        public List<string> Calls { get; } = new();

        public Task<PluginCommandResult> ExecuteAsync(
            string commandKey,
            PluginCommandInvocation? invocation = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(commandKey);
            return Task.FromResult(_execute(commandKey, cancellationToken));
        }
    }

    private sealed class CapturingRunner : IWorkflowCommandRunner
    {
        private readonly Func<string, PluginCommandInvocation?, PluginCommandResult> _execute;

        public CapturingRunner(Func<string, PluginCommandInvocation?, PluginCommandResult> execute)
        {
            _execute = execute;
        }

        public List<string> Calls { get; } = new();
        public List<PluginCommandInvocation?> Invocations { get; } = new();

        public Task<PluginCommandResult> ExecuteAsync(
            string commandKey,
            PluginCommandInvocation? invocation = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(commandKey);
            Invocations.Add(invocation);
            return Task.FromResult(_execute(commandKey, invocation));
        }
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "workflow";
        public string Name => "Workflow";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }

    private sealed class ExtraPlugin : ILongPlugin
    {
        public string Id => "extra";
        public string Name => "Extra";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }

}
