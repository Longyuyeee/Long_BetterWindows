using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record WorkflowBindingResolution(
        bool IsSuccess,
        PluginCommandInvocation? Invocation,
        string? Error);

    /// <summary>Validates plugin outputs and resolves authorized bindings without exposing values.</summary>
    public static class CommandWorkflowBindingResolver
    {
        public const int MaximumOutputCount = 64;
        public const int MaximumOutputValueLength = 65536;

        public static bool TrySnapshotOutputs(
            PluginCommandResult result,
            out IReadOnlyDictionary<string, PluginCommandOutput> outputs,
            out string? error)
        {
            ArgumentNullException.ThrowIfNull(result);
            var source = result.Outputs ?? new Dictionary<string, PluginCommandOutput>();
            if (source.Count > MaximumOutputCount)
            {
                outputs = new Dictionary<string, PluginCommandOutput>();
                error = $"Command returned more than {MaximumOutputCount} structured outputs.";
                return false;
            }
            var snapshot = new Dictionary<string, PluginCommandOutput>(StringComparer.Ordinal);
            foreach (var item in source)
            {
                if (!IsIdentifier(item.Key))
                {
                    outputs = new Dictionary<string, PluginCommandOutput>();
                    error = "Command returned an invalid structured output key.";
                    return false;
                }
                if (item.Value is null
                    || !Enum.IsDefined(item.Value.Type)
                    || item.Value.Value is null
                    || item.Value.Value.Length > MaximumOutputValueLength
                    || (item.Value.Type == PluginCommandOutputType.Path
                        && string.IsNullOrWhiteSpace(item.Value.Value)))
                {
                    outputs = new Dictionary<string, PluginCommandOutput>();
                    error = "Command returned an invalid structured output value.";
                    return false;
                }
                snapshot.Add(item.Key, new PluginCommandOutput(item.Value.Type, item.Value.Value));
            }
            outputs = snapshot;
            error = null;
            return true;
        }

        public static bool TrySnapshotDeclaredOutputs(
            PluginCommandResult result,
            IReadOnlyList<PluginCommandOutputDeclaration> declarations,
            out IReadOnlyDictionary<string, PluginCommandOutput> outputs,
            out string? error)
        {
            ArgumentNullException.ThrowIfNull(declarations);
            if (!TrySnapshotOutputs(result, out outputs, out error)) return false;
            var declared = declarations
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);
            foreach (var output in outputs)
            {
                if (!declared.TryGetValue(output.Key, out var type))
                {
                    outputs = new Dictionary<string, PluginCommandOutput>();
                    error = "Command returned an undeclared structured output.";
                    return false;
                }
                if (type != output.Value.Type)
                {
                    outputs = new Dictionary<string, PluginCommandOutput>();
                    error = "Command returned a structured output with the wrong declared type.";
                    return false;
                }
            }
            return true;
        }

        public static WorkflowBindingResolution Resolve(
            WorkflowCommand command,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, PluginCommandOutput>> stepOutputs)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(stepOutputs);
            if (command.Invocation is null)
                return Failure("Workflow command invocation is missing.");
            var bindings = command.Bindings ?? Array.Empty<WorkflowValueBinding>();
            if (bindings.Count == 0)
                return new WorkflowBindingResolution(true, Clone(command.Invocation), null);

            var text = command.Invocation.Text;
            var paths = command.Invocation.Paths.ToList();
            var arguments = new Dictionary<string, string>(
                command.Invocation.Arguments,
                StringComparer.Ordinal);
            foreach (var binding in bindings)
            {
                if (!stepOutputs.TryGetValue(binding.SourceStepId, out var source)
                    || !source.TryGetValue(binding.OutputKey, out var output))
                {
                    return Failure("A required workflow output is unavailable.");
                }
                switch (binding.Target)
                {
                    case WorkflowBindingTarget.Text when output.Type == PluginCommandOutputType.Text:
                        text = output.Value;
                        break;
                    case WorkflowBindingTarget.Path when output.Type == PluginCommandOutputType.Path:
                        paths.Add(output.Value);
                        break;
                    case WorkflowBindingTarget.Argument when output.Type == PluginCommandOutputType.Text:
                        arguments[binding.ArgumentKey!] = output.Value;
                        break;
                    default:
                        return Failure("A workflow output type does not match its binding target.");
                }
            }
            if (paths.Count > 64 || arguments.Count > 64)
                return Failure("Resolved workflow inputs exceed the invocation limits.");
            return new WorkflowBindingResolution(
                true,
                new PluginCommandInvocation
                {
                    CommandId = command.Invocation.CommandId,
                    InputType = command.Invocation.InputType,
                    Text = text,
                    Paths = paths,
                    ImagePng = command.Invocation.ImagePng?.ToArray(),
                    Arguments = arguments,
                },
                null);
        }

        private static PluginCommandInvocation Clone(PluginCommandInvocation invocation)
            => new()
            {
                CommandId = invocation.CommandId,
                InputType = invocation.InputType,
                Text = invocation.Text,
                Paths = invocation.Paths.ToArray(),
                ImagePng = invocation.ImagePng?.ToArray(),
                Arguments = new Dictionary<string, string>(invocation.Arguments, StringComparer.Ordinal),
            };

        private static WorkflowBindingResolution Failure(string error)
            => new(false, null, error);

        private static bool IsIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 64
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');
    }
}
