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

            var issues = new List<string>();
            var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsValidIdentifier(workflow.Id))
                issues.Add("Workflow id must contain 1-64 letters, digits, dots, underscores, or hyphens.");
            if (string.IsNullOrWhiteSpace(workflow.Name))
                issues.Add("Workflow name must not be empty.");
            if (workflow.Steps.Count is < 1 or > MaximumStepCount)
                issues.Add($"Workflow must contain between 1 and {MaximumStepCount} steps.");

            var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in workflow.Steps)
            {
                if (!IsValidIdentifier(step.Id))
                    issues.Add("Workflow step id must contain 1-64 letters, digits, dots, underscores, or hyphens.");
                else if (!stepIds.Add(step.Id))
                    issues.Add($"Workflow step id is duplicated: {step.Id}");

                ValidateCommand(step.Id, "command", step.Command, issues, pluginIds);
                if (step.Compensation is not null)
                    ValidateCommand(step.Id, "compensation", step.Compensation, issues, pluginIds);
                if (workflow.FailureMode == WorkflowFailureMode.Compensate
                    && step.Effect == WorkflowStepEffect.Mutating
                    && step.Compensation is null)
                {
                    issues.Add($"Mutating workflow step requires compensation: {step.Id}");
                }
            }

            var permissions = pluginIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id =>
                {
                    var entry = _plugins.Get(id)!;
                    return new WorkflowPermissionRequirement(
                        id,
                        entry.Manifest.Version,
                        entry.Manifest.Capabilities
                            .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                            .ToList());
                })
                .ToList();

            return new CommandWorkflowPreflightResult(
                issues.Count == 0,
                ComputeFingerprint(workflow, permissions),
                issues,
                permissions);
        }

        private void ValidateCommand(
            string stepId,
            string role,
            WorkflowCommand? command,
            ICollection<string> issues,
            ISet<string> pluginIds)
        {
            if (command is null || string.IsNullOrWhiteSpace(command.CommandKey))
            {
                issues.Add($"Workflow step {role} is missing: {stepId}");
                return;
            }

            var descriptor = _plugins.Commands.Get(command.CommandKey);
            if (descriptor is null)
            {
                issues.Add($"Workflow step {role} command was not found: {stepId}");
                return;
            }
            if (_plugins.Get(descriptor.PluginId) is null)
            {
                issues.Add($"Workflow step {role} plugin is not loaded: {stepId}");
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
                issues.Add($"Workflow step {role} command id does not match its target: {stepId}");
                return;
            }
            if (invocation.InputType != AcceptedInputType.None
                && !descriptor.Command.AcceptedInputs.Contains(invocation.InputType))
            {
                issues.Add($"Workflow step {role} input type is not accepted: {stepId}");
            }
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            return value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
        }

        private static string ComputeFingerprint(
            CommandWorkflowDefinition workflow,
            IReadOnlyList<WorkflowPermissionRequirement> permissions)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteField(writer, "long-command-workflow-v1");
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
                    writer.Write(permission.Capabilities.Count);
                    foreach (var capability in permission.Capabilities)
                        WriteField(writer, capability);
                }
            }

            return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        }

        private static void WriteCommand(BinaryWriter writer, WorkflowCommand? command)
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
        }

        private static void WriteField(BinaryWriter writer, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
