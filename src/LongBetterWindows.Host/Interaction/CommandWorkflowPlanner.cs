using System.IO;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>Validates a workflow and produces its immutable permission-review identity.</summary>
    public sealed class CommandWorkflowPlanner
    {
        public const int MaximumStepCount = 32;

        private readonly PluginRegistry _plugins;

        public CommandWorkflowPlanner(PluginRegistry plugins)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        }

        public CommandWorkflowPreflightResult Preflight(CommandWorkflowDefinition workflow)
        {
            ArgumentNullException.ThrowIfNull(workflow);

            var catalogRevision = _plugins.CatalogRevision;
            var issues = new List<CommandWorkflowPreflightIssue>();
            var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsValidIdentifier(workflow.Id))
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightDefinitionInvalid,
                    "Workflow id must contain 1-64 letters, digits, dots, underscores, or hyphens.");
            if (string.IsNullOrWhiteSpace(workflow.Name))
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightDefinitionInvalid,
                    "Workflow name must not be empty.");
            if (workflow.Steps.Count is < 1 or > MaximumStepCount)
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightDefinitionInvalid,
                    $"Workflow must contain between 1 and {MaximumStepCount} steps.");

            var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var availableOutputs = new Dictionary<
                string,
                IReadOnlyDictionary<string, PluginCommandOutputType>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var step in workflow.Steps)
            {
                if (!IsValidIdentifier(step.Id))
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightDefinitionInvalid,
                        "Workflow step id must contain 1-64 letters, digits, dots, underscores, or hyphens.");
                else if (!stepIds.Add(step.Id))
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightDefinitionInvalid,
                        $"Workflow step id is duplicated: {step.Id}");

                ValidateCommand(
                    step.Id,
                    "command",
                    step.Command,
                    availableOutputs,
                    issues,
                    pluginIds);
                var compensationOutputs = new Dictionary<
                    string,
                    IReadOnlyDictionary<string, PluginCommandOutputType>>(
                    availableOutputs,
                    StringComparer.OrdinalIgnoreCase);
                compensationOutputs[step.Id] = GetDeclaredOutputs(step.Command);
                if (step.Compensation is not null)
                    ValidateCommand(
                        step.Id,
                        "compensation",
                        step.Compensation,
                        compensationOutputs,
                        issues,
                        pluginIds);
                if (workflow.FailureMode == WorkflowFailureMode.Compensate
                    && step.Effect == WorkflowStepEffect.Mutating
                    && step.Compensation is null)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightCompensationRequired,
                        $"Mutating workflow step requires compensation: {step.Id}");
                }
                availableOutputs[step.Id] = GetDeclaredOutputs(step.Command);
            }

            var permissions = new List<WorkflowPermissionRequirement>();
            foreach (var id in pluginIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                var entry = _plugins.Get(id);
                if (entry is null)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightCatalogChanged,
                        $"Workflow plugin changed during preflight: {id}");
                    continue;
                }
                permissions.Add(new WorkflowPermissionRequirement(
                    id,
                    entry.Manifest.Version,
                    entry.RegistrationRevision,
                    entry.Manifest.Capabilities
                        .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                        .ToList()));
            }
            if (_plugins.CatalogRevision != catalogRevision)
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightCatalogChanged,
                    "Plugin catalog changed during workflow preflight.");

            return new CommandWorkflowPreflightResult(
                issues.Count == 0,
                ComputeFingerprint(workflow, permissions),
                issues.Select(issue => issue.Message).ToArray(),
                permissions)
            {
                IssueDetails = issues.ToArray(),
            };
        }

        private void ValidateCommand(
            string stepId,
            string role,
            WorkflowCommand? command,
            IReadOnlyDictionary<
                string,
                IReadOnlyDictionary<string, PluginCommandOutputType>> availableOutputs,
            ICollection<CommandWorkflowPreflightIssue> issues,
            ISet<string> pluginIds)
        {
            if (command is null || string.IsNullOrWhiteSpace(command.CommandKey))
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightCommandInvalid,
                    $"Workflow step {role} is missing: {stepId}");
                return;
            }

            var descriptor = _plugins.Commands.Get(command.CommandKey);
            if (descriptor is null)
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightCommandInvalid,
                    $"Workflow step {role} command was not found: {stepId}");
                return;
            }
            if (_plugins.Get(descriptor.PluginId) is null)
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightPluginUnavailable,
                    $"Workflow step {role} plugin is not loaded: {stepId}");
                return;
            }

            pluginIds.Add(descriptor.PluginId);
            var invocation = command.Invocation;
            if (invocation is null
                || !string.Equals(
                    invocation.CommandId,
                    descriptor.Command.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightCommandInvalid,
                    $"Workflow step {role} command id does not match its target: {stepId}");
                return;
            }
            if (!descriptor.Command.AcceptedInputs.Contains(invocation.InputType))
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightInputInvalid,
                    $"Workflow step {role} input type is not accepted: {stepId}");
            }
            var deferredArgumentKeys = (command.Bindings ?? Array.Empty<WorkflowValueBinding>())
                .Where(binding => binding.Target == WorkflowBindingTarget.Argument
                    && !string.IsNullOrWhiteSpace(binding.ArgumentKey))
                .Select(binding => binding.ArgumentKey!);
            var parameterResult = PluginCommandArgumentValidator.ValidateForWorkflowPreflight(
                descriptor.Command.ArgumentSchema,
                invocation.Arguments,
                deferredArgumentKeys);
            foreach (var issue in parameterResult.Issues)
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightArgumentInvalid,
                    $"Workflow step {role} parameters are invalid: {stepId}. {issue}");
            }
            ValidateBindings(
                stepId,
                role,
                command.Bindings,
                invocation.InputType,
                descriptor.Command.ArgumentSchema,
                availableOutputs,
                issues);
        }

        private IReadOnlyDictionary<string, PluginCommandOutputType> GetDeclaredOutputs(
            WorkflowCommand? command)
        {
            var descriptor = command is null ? null : _plugins.Commands.Get(command.CommandKey);
            return descriptor?.Command.Outputs
                .GroupBy(output => output.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Type,
                    StringComparer.Ordinal)
                ?? new Dictionary<string, PluginCommandOutputType>();
        }

        private static void ValidateBindings(
            string stepId,
            string role,
            IReadOnlyList<WorkflowValueBinding>? bindings,
            AcceptedInputType inputType,
            IReadOnlyList<PluginCommandArgumentDeclaration>? argumentSchema,
            IReadOnlyDictionary<
                string,
                IReadOnlyDictionary<string, PluginCommandOutputType>> availableOutputs,
            ICollection<CommandWorkflowPreflightIssue> issues)
        {
            bindings ??= Array.Empty<WorkflowValueBinding>();
            if (bindings.Count > 64)
            {
                AddIssue(
                    issues,
                    WorkflowErrorCode.PreflightBindingInvalid,
                    $"Workflow step {role} has more than 64 bindings: {stepId}");
                return;
            }
            var textTargets = 0;
            var argumentTargets = new HashSet<string>(StringComparer.Ordinal);
            var declaredArgumentKeys = (argumentSchema
                    ?? Array.Empty<PluginCommandArgumentDeclaration>())
                .Select(declaration => declaration.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                PluginCommandOutputType? outputType = null;
                if (!availableOutputs.TryGetValue(binding.SourceStepId, out var sourceOutputs))
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} binding source must already be available: {stepId}");
                }
                else if (!sourceOutputs.TryGetValue(binding.OutputKey, out var declaredType))
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} binding output is not declared: {stepId}");
                }
                else
                {
                    outputType = declaredType;
                }
                if (!IsValidIdentifier(binding.OutputKey))
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} binding output key is invalid: {stepId}");
                if (!Enum.IsDefined(binding.Target))
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} binding target is invalid: {stepId}");
                    continue;
                }
                if (binding.Target == WorkflowBindingTarget.Text && ++textTargets > 1)
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} has duplicate text bindings: {stepId}");
                if (binding.Target == WorkflowBindingTarget.Text
                    && inputType is AcceptedInputType.None or AcceptedInputType.Image)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} text binding is incompatible with its input type: {stepId}");
                }
                if (binding.Target == WorkflowBindingTarget.Text
                    && outputType.HasValue
                    && outputType.Value != PluginCommandOutputType.Text)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} text binding output type is incompatible: {stepId}");
                }
                if (binding.Target == WorkflowBindingTarget.Path
                    && inputType is not (AcceptedInputType.File
                        or AcceptedInputType.Files
                        or AcceptedInputType.Folder
                        or AcceptedInputType.ExplorerSelection))
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} path binding is incompatible with its input type: {stepId}");
                }
                if (binding.Target == WorkflowBindingTarget.Path
                    && outputType.HasValue
                    && outputType.Value != PluginCommandOutputType.Path)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} path binding output type is incompatible: {stepId}");
                }
                if (binding.Target == WorkflowBindingTarget.Argument)
                {
                    if (string.IsNullOrWhiteSpace(binding.ArgumentKey)
                        || binding.ArgumentKey.Length > 128)
                    {
                        AddIssue(
                            issues,
                            WorkflowErrorCode.PreflightBindingInvalid,
                            $"Workflow step {role} binding argument key is invalid: {stepId}");
                    }
                    else if (!argumentTargets.Add(binding.ArgumentKey))
                    {
                        AddIssue(
                            issues,
                            WorkflowErrorCode.PreflightBindingInvalid,
                            $"Workflow step {role} has duplicate argument bindings: {stepId}");
                    }
                    else if (declaredArgumentKeys.Count > 0
                        && !declaredArgumentKeys.Contains(binding.ArgumentKey))
                    {
                        AddIssue(
                            issues,
                            WorkflowErrorCode.PreflightBindingInvalid,
                            $"Workflow step {role} binding argument target is not declared: {stepId}");
                    }
                    if (outputType.HasValue
                        && outputType.Value != PluginCommandOutputType.Text)
                    {
                        AddIssue(
                            issues,
                            WorkflowErrorCode.PreflightBindingInvalid,
                            $"Workflow step {role} argument binding output type is incompatible: {stepId}");
                    }
                }
                else if (binding.ArgumentKey is not null)
                {
                    AddIssue(
                        issues,
                        WorkflowErrorCode.PreflightBindingInvalid,
                        $"Workflow step {role} non-argument binding has an argument key: {stepId}");
                }
            }
        }

        private static void AddIssue(
            ICollection<CommandWorkflowPreflightIssue> issues,
            WorkflowErrorCode errorCode,
            string technicalMessage)
            => issues.Add(new CommandWorkflowPreflightIssue(errorCode, technicalMessage));

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            return value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
        }

        private string ComputeFingerprint(
            CommandWorkflowDefinition workflow,
            IReadOnlyList<WorkflowPermissionRequirement> permissions)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteField(writer, "long-command-workflow-v2");
                WriteField(writer, workflow.Id);
                WriteField(writer, workflow.Name);
                writer.Write((int)workflow.FailureMode);
                writer.Write(workflow.Steps.Count);
                foreach (var step in workflow.Steps)
                {
                    WriteField(writer, step.Id);
                    writer.Write((int)step.Effect);
                    WriteCommand(writer, step.Command);
                    writer.Write(step.Compensation is not null);
                    if (step.Compensation is not null) WriteCommand(writer, step.Compensation);
                }
                writer.Write(permissions.Count);
                foreach (var permission in permissions)
                {
                    WriteField(writer, permission.PluginId);
                    WriteField(writer, permission.PluginVersion);
                    writer.Write(permission.RegistrationRevision);
                    writer.Write(permission.Capabilities.Count);
                    foreach (var capability in permission.Capabilities)
                        WriteField(writer, capability);
                }
            }

            return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        }

        private void WriteCommand(BinaryWriter writer, WorkflowCommand? command)
        {
            writer.Write(command is not null);
            if (command is null) return;
            WriteField(writer, command.CommandKey);
            var invocation = command.Invocation;
            writer.Write(invocation is not null);
            if (invocation is null) return;
            WriteField(writer, invocation.CommandId);
            writer.Write((int)invocation.InputType);
            WriteField(writer, invocation.Text ?? string.Empty);
            writer.Write(invocation.Paths.Count);
            foreach (var path in invocation.Paths) WriteField(writer, path);
            var image = invocation.ImagePng ?? Array.Empty<byte>();
            writer.Write(image.Length);
            writer.Write(image);
            writer.Write(invocation.Arguments.Count);
            foreach (var argument in invocation.Arguments.OrderBy(
                         entry => entry.Key,
                         StringComparer.Ordinal))
            {
                WriteField(writer, argument.Key);
                WriteField(writer, argument.Value);
            }
            var bindings = command.Bindings ?? Array.Empty<WorkflowValueBinding>();
            writer.Write(bindings.Count);
            foreach (var binding in bindings)
            {
                WriteField(writer, binding.SourceStepId);
                WriteField(writer, binding.OutputKey);
                writer.Write((int)binding.Target);
                WriteField(writer, binding.ArgumentKey ?? string.Empty);
            }
            var descriptor = _plugins.Commands.Get(command.CommandKey);
            WriteArgumentSchema(writer, descriptor?.Command.ArgumentSchema);
        }

        private static void WriteArgumentSchema(
            BinaryWriter writer,
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema)
        {
            schema ??= Array.Empty<PluginCommandArgumentDeclaration>();
            writer.Write(schema.Count);
            foreach (var declaration in schema)
            {
                WriteField(writer, declaration.Key);
                WriteField(writer, declaration.Name);
                WriteField(writer, declaration.Description ?? string.Empty);
                writer.Write((int)declaration.Type);
                writer.Write(declaration.Required);
                writer.Write(declaration.DefaultValue is not null);
                if (declaration.DefaultValue is not null)
                    WriteField(writer, declaration.DefaultValue);
                writer.Write(declaration.Sensitive);
                WriteNullableDecimal(writer, declaration.Minimum);
                WriteNullableDecimal(writer, declaration.Maximum);
                WriteNullableInt32(writer, declaration.MinLength);
                WriteNullableInt32(writer, declaration.MaxLength);
                var values = declaration.EnumValues ?? new List<string>();
                writer.Write(values.Count);
                foreach (var value in values) WriteField(writer, value);
            }
        }

        private static void WriteNullableDecimal(BinaryWriter writer, decimal? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value);
        }

        private static void WriteNullableInt32(BinaryWriter writer, int? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value);
        }

        private static void WriteField(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
