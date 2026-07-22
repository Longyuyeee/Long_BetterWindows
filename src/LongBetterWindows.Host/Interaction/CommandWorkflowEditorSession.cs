using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    public enum WorkflowCommandRole
    {
        Primary,
        Compensation,
    }

    public sealed record CommandWorkflowImportReview(
        bool IsSuccess,
        string SourcePath,
        CommandWorkflowDefinition? Workflow,
        WorkflowDocumentSource? Source,
        WorkflowDocumentTrustLevel TrustLevel,
        string DefinitionSha256,
        int? MigratedFromSchemaVersion,
        CommandWorkflowPreflightResult? Preflight,
        bool ContainsSensitiveInputs,
        string? Error);

    public sealed record CommandWorkflowEditorState(
        CommandWorkflowDefinition? Draft,
        string? ExistingDefinitionSha256,
        bool IsDirty,
        CommandWorkflowPreflightResult? Preflight,
        string? Error)
    {
        public bool CanSave => Draft is not null && Preflight?.IsValid == true;
    }

    /// <summary>Owns an editable workflow draft independently from WPF controls.</summary>
    public sealed class CommandWorkflowEditorSession
    {
        private readonly PluginRegistry _plugins;
        private readonly CommandWorkflowRepository _repository;
        private readonly CommandWorkflowPlanner _planner;

        public CommandWorkflowEditorSession(
            PluginRegistry plugins,
            CommandWorkflowRepository repository)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _planner = new CommandWorkflowPlanner(plugins);
            State = new CommandWorkflowEditorState(null, null, false, null, null);
        }

        public CommandWorkflowEditorState State { get; private set; }

        public IReadOnlyList<CommandDescriptor> AvailableCommands
            => _plugins.Commands.GetAll();

        public void StartNew(string id, string name)
        {
            var draft = new CommandWorkflowDefinition(
                id?.Trim() ?? string.Empty,
                name?.Trim() ?? string.Empty,
                WorkflowFailureMode.Stop,
                Array.Empty<CommandWorkflowStep>());
            SetDraft(draft, existingHash: null, isDirty: true);
        }

        public async Task<bool> LoadAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            var result = await _repository.LoadManagedAsync(workflowId, cancellationToken);
            if (!result.IsSuccess)
            {
                State = State with { Error = result.Error };
                return false;
            }
            SetDraft(result.Workflow!, result.DefinitionSha256, isDirty: false);
            return true;
        }

        public void UpdateIdentity(string id, string name)
        {
            var draft = RequireDraft();
            var normalizedId = id?.Trim() ?? string.Empty;
            if (State.ExistingDefinitionSha256 is not null
                && !string.Equals(draft.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                State = State with { Error = "A saved workflow id cannot be changed." };
                return;
            }
            var normalizedName = name?.Trim() ?? string.Empty;
            if (string.Equals(draft.Id, normalizedId, StringComparison.Ordinal)
                && string.Equals(draft.Name, normalizedName, StringComparison.Ordinal)) return;
            SetDraft(draft with
            {
                Id = normalizedId,
                Name = normalizedName,
            }, State.ExistingDefinitionSha256, isDirty: true);
        }

        public void SetFailureMode(WorkflowFailureMode failureMode)
        {
            if (!Enum.IsDefined(failureMode))
                throw new ArgumentOutOfRangeException(nameof(failureMode));
            if (RequireDraft().FailureMode == failureMode) return;
            SetDraft(
                RequireDraft() with { FailureMode = failureMode },
                State.ExistingDefinitionSha256,
                isDirty: true);
        }

        public bool AddStep(
            string commandKey,
            WorkflowStepEffect effect = WorkflowStepEffect.ReadOnly)
        {
            if (!Enum.IsDefined(effect))
                throw new ArgumentOutOfRangeException(nameof(effect));
            var draft = RequireDraft();
            if (draft.Steps.Count >= CommandWorkflowPlanner.MaximumStepCount)
            {
                State = State with
                {
                    Error = $"A workflow cannot contain more than {CommandWorkflowPlanner.MaximumStepCount} steps.",
                };
                return false;
            }
            var command = CreateCommand(commandKey);
            if (command is null)
            {
                State = State with { Error = $"Command was not found: {commandKey}" };
                return false;
            }
            var steps = draft.Steps.ToList();
            steps.Add(new CommandWorkflowStep(
                NextStepId(steps),
                effect,
                command));
            SetDraft(draft with { Steps = steps }, State.ExistingDefinitionSha256, isDirty: true);
            return true;
        }

        public bool UpdateStep(
            string stepId,
            WorkflowStepEffect effect,
            string commandKey,
            string? compensationCommandKey)
        {
            if (!Enum.IsDefined(effect))
                throw new ArgumentOutOfRangeException(nameof(effect));
            var draft = RequireDraft();
            var index = FindStep(draft, stepId);
            if (index < 0) return false;
            var existing = draft.Steps[index];
            var command = string.Equals(
                existing.Command?.CommandKey,
                commandKey,
                StringComparison.OrdinalIgnoreCase)
                ? existing.Command
                : CreateCommand(commandKey);
            var compensation = string.IsNullOrWhiteSpace(compensationCommandKey)
                ? null
                : string.Equals(
                    existing.Compensation?.CommandKey,
                    compensationCommandKey,
                    StringComparison.OrdinalIgnoreCase)
                    ? existing.Compensation
                    : CreateCommand(compensationCommandKey);
            if (command is null || (!string.IsNullOrWhiteSpace(compensationCommandKey) && compensation is null))
            {
                State = State with { Error = "A selected workflow command is no longer available." };
                return false;
            }
            if (existing.Effect == effect
                && string.Equals(existing.Command?.CommandKey, command.CommandKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    existing.Compensation?.CommandKey,
                    compensation?.CommandKey,
                    StringComparison.OrdinalIgnoreCase)) return true;
            var steps = draft.Steps.ToList();
            steps[index] = steps[index] with
            {
                Effect = effect,
                Command = command,
                Compensation = effect == WorkflowStepEffect.Mutating ? compensation : null,
            };
            SetDraft(draft with { Steps = steps }, State.ExistingDefinitionSha256, isDirty: true);
            return true;
        }

        public bool UpdateInvocation(
            string stepId,
            WorkflowCommandRole role,
            AcceptedInputType inputType,
            string? text = null,
            IReadOnlyList<string>? paths = null,
            byte[]? imagePng = null,
            IReadOnlyDictionary<string, string>? arguments = null)
        {
            if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (!Enum.IsDefined(inputType)) throw new ArgumentOutOfRangeException(nameof(inputType));
            var draft = RequireDraft();
            var index = FindStep(draft, stepId);
            if (index < 0) return false;
            var step = draft.Steps[index];
            var command = role == WorkflowCommandRole.Primary
                ? step.Command
                : step.Compensation;
            if (command?.Invocation is null)
            {
                State = State with { Error = $"Workflow step {role.ToString().ToLowerInvariant()} is not configured: {stepId}" };
                return false;
            }
            var descriptor = _plugins.Commands.Get(command.CommandKey);
            if (descriptor is null)
            {
                State = State with { Error = $"Command was not found: {command.CommandKey}" };
                return false;
            }
            if (!descriptor.Command.AcceptedInputs.Contains(inputType))
            {
                State = State with { Error = $"Command does not accept {inputType} input: {command.CommandKey}" };
                return false;
            }

            var invocation = new PluginCommandInvocation
            {
                CommandId = command.Invocation.CommandId,
                InputType = inputType,
                Text = text,
                Paths = paths?.ToArray() ?? Array.Empty<string>(),
                ImagePng = imagePng?.ToArray(),
                Arguments = arguments is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(arguments, StringComparer.Ordinal),
            };
            if (InvocationEquals(command.Invocation, invocation)) return true;

            var updatedCommand = command with { Invocation = invocation };
            var steps = draft.Steps.ToList();
            steps[index] = role == WorkflowCommandRole.Primary
                ? step with { Command = updatedCommand }
                : step with { Compensation = updatedCommand };
            SetDraft(draft with { Steps = steps }, State.ExistingDefinitionSha256, isDirty: true);
            return true;
        }

        public async Task<CommandWorkflowImportReview> PreviewImportAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            var result = await _repository.ImportAsync(sourcePath, cancellationToken);
            if (!result.IsSuccess)
            {
                return new CommandWorkflowImportReview(
                    false,
                    sourcePath,
                    null,
                    result.Source,
                    result.TrustLevel,
                    result.DefinitionSha256,
                    result.MigratedFromSchemaVersion,
                    null,
                    false,
                    result.Error);
            }
            var workflow = result.Workflow!;
            return new CommandWorkflowImportReview(
                true,
                sourcePath,
                workflow,
                result.Source,
                result.TrustLevel,
                result.DefinitionSha256,
                result.MigratedFromSchemaVersion,
                _planner.Preflight(workflow),
                CommandWorkflowDocumentCodec.ContainsSensitiveInputs(workflow),
                null);
        }

        public bool AdoptImport(CommandWorkflowImportReview review)
        {
            ArgumentNullException.ThrowIfNull(review);
            if (!review.IsSuccess || review.Workflow is null)
            {
                State = State with { Error = review.Error ?? "Workflow import review is invalid." };
                return false;
            }
            SetDraft(review.Workflow, existingHash: null, isDirty: true);
            return true;
        }

        public bool RemoveStep(string stepId)
        {
            var draft = RequireDraft();
            var steps = draft.Steps.ToList();
            var removed = steps.RemoveAll(step => string.Equals(
                step.Id,
                stepId,
                StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
                SetDraft(draft with { Steps = steps }, State.ExistingDefinitionSha256, isDirty: true);
            return removed;
        }

        public bool MoveStep(string stepId, int offset)
        {
            if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset));
            var draft = RequireDraft();
            var steps = draft.Steps.ToList();
            var index = FindStep(draft, stepId);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= steps.Count) return false;
            (steps[index], steps[target]) = (steps[target], steps[index]);
            SetDraft(draft with { Steps = steps }, State.ExistingDefinitionSha256, isDirty: true);
            return true;
        }

        public async Task<CommandWorkflowSaveResult> SaveAsync(
            bool allowSensitiveInputs,
            CancellationToken cancellationToken = default)
        {
            var draft = RequireDraft();
            var preflight = _planner.Preflight(draft);
            if (!preflight.IsValid)
            {
                var error = string.Join(" ", preflight.Issues);
                State = State with { Preflight = preflight, Error = error };
                return new CommandWorkflowSaveResult(false, null, string.Empty, error);
            }
            var result = await _repository.SaveAsync(
                draft,
                new CommandWorkflowSaveOptions(
                    allowSensitiveInputs,
                    State.ExistingDefinitionSha256),
                cancellationToken);
            if (result.IsSuccess)
            {
                SetDraft(draft, result.DefinitionSha256, isDirty: false);
            }
            else
            {
                State = State with { Error = result.Error };
            }
            return result;
        }

        public async Task<CommandWorkflowDeleteResult> DeleteCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            var draft = RequireDraft();
            if (State.ExistingDefinitionSha256 is null)
            {
                const string error = "The workflow has not been saved.";
                State = State with { Error = error };
                return new CommandWorkflowDeleteResult(false, error);
            }
            var result = await _repository.DeleteManagedAsync(
                draft.Id,
                State.ExistingDefinitionSha256,
                cancellationToken);
            if (result.IsSuccess)
                State = new CommandWorkflowEditorState(null, null, false, null, null);
            else
                State = State with { Error = result.Error };
            return result;
        }

        public void RefreshPreflight()
        {
            if (State.Draft is null) return;
            SetDraft(State.Draft, State.ExistingDefinitionSha256, State.IsDirty);
        }

        private WorkflowCommand? CreateCommand(string commandKey)
        {
            var descriptor = _plugins.Commands.Get(commandKey);
            return descriptor is null
                ? null
                : new WorkflowCommand(
                    descriptor.Key,
                    new PluginCommandInvocation { CommandId = descriptor.Command.Id });
        }

        private void SetDraft(
            CommandWorkflowDefinition draft,
            string? existingHash,
            bool isDirty)
        {
            State = new CommandWorkflowEditorState(
                draft,
                existingHash,
                isDirty,
                _planner.Preflight(draft),
                null);
        }

        private CommandWorkflowDefinition RequireDraft()
            => State.Draft ?? throw new InvalidOperationException("No workflow draft is open.");

        private static int FindStep(CommandWorkflowDefinition draft, string stepId)
        {
            for (var index = 0; index < draft.Steps.Count; index++)
            {
                if (string.Equals(
                    draft.Steps[index].Id,
                    stepId,
                    StringComparison.OrdinalIgnoreCase)) return index;
            }
            return -1;
        }

        private static string NextStepId(IReadOnlyCollection<CommandWorkflowStep> steps)
        {
            var used = steps.Select(step => step.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var number = 1; number <= CommandWorkflowPlanner.MaximumStepCount; number++)
            {
                var candidate = $"step-{number}";
                if (!used.Contains(candidate)) return candidate;
            }
            return $"step-{steps.Count + 1}";
        }

        private static bool InvocationEquals(
            PluginCommandInvocation first,
            PluginCommandInvocation second)
            => string.Equals(first.CommandId, second.CommandId, StringComparison.Ordinal)
                && first.InputType == second.InputType
                && string.Equals(first.Text, second.Text, StringComparison.Ordinal)
                && first.Paths.SequenceEqual(second.Paths, StringComparer.Ordinal)
                && (first.ImagePng ?? Array.Empty<byte>()).SequenceEqual(
                    second.ImagePng ?? Array.Empty<byte>())
                && first.Arguments.Count == second.Arguments.Count
                && first.Arguments.All(argument =>
                    second.Arguments.TryGetValue(argument.Key, out var value)
                    && string.Equals(argument.Value, value, StringComparison.Ordinal));
    }
}
