using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    public enum WorkflowDocumentSourceKind
    {
        LocalManaged,
        Imported,
    }

    public enum WorkflowDocumentTrustLevel
    {
        Untrusted,
        LocalManaged,
        TrustedSource,
    }

    public sealed record WorkflowDocumentSource(
        WorkflowDocumentSourceKind Kind,
        string SourceId);

    public sealed record WorkflowDocumentEnvelope(
        int SchemaVersion,
        WorkflowDocumentSource Source,
        CommandWorkflowDefinition Workflow);

    public sealed record WorkflowDocumentReadResult(
        bool IsSuccess,
        CommandWorkflowDefinition? Workflow,
        WorkflowDocumentSource? Source,
        WorkflowDocumentTrustLevel TrustLevel,
        string DefinitionSha256,
        int? MigratedFromSchemaVersion,
        string? Error);

    public sealed class WorkflowSourceTrustPolicy
    {
        private readonly Dictionary<string, HashSet<string>> _trustedHashes;

        public WorkflowSourceTrustPolicy(
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> trustedHashes)
        {
            ArgumentNullException.ThrowIfNull(trustedHashes);
            _trustedHashes = trustedHashes.ToDictionary(
                entry => entry.Key,
                entry => new HashSet<string>(
                    entry.Value.Select(NormalizeHash),
                    StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
        }

        public static WorkflowSourceTrustPolicy Empty { get; } = new(
            new Dictionary<string, IReadOnlyCollection<string>>());

        public bool IsTrusted(string sourceId, string definitionSha256)
            => !string.IsNullOrWhiteSpace(sourceId)
                && _trustedHashes.TryGetValue(sourceId, out var hashes)
                && hashes.Contains(NormalizeHash(definitionSha256));

        private static string NormalizeHash(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
                throw new ArgumentException("Trusted workflow hashes must be 64 hexadecimal characters.");
            return normalized;
        }
    }

    public static class CommandWorkflowDocumentCodec
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaximumImageBytes = 2 * 1024 * 1024;

        private static readonly JsonSerializerOptions CompactJson = CreateOptions(writeIndented: false);
        private static readonly JsonSerializerOptions IndentedJson = CreateOptions(writeIndented: true);

        public static string Serialize(
            CommandWorkflowDefinition workflow,
            WorkflowDocumentSource source)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            ArgumentNullException.ThrowIfNull(source);
            var normalized = Normalize(workflow);
            var envelope = new WorkflowDocumentEnvelope(CurrentSchemaVersion, source, normalized);
            return JsonSerializer.Serialize(envelope, IndentedJson);
        }

        public static WorkflowDocumentReadResult Deserialize(
            string json,
            bool isManagedFile,
            WorkflowSourceTrustPolicy? trustPolicy = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return Failure("Workflow document is empty.");
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
                if (!document.RootElement.TryGetProperty("schema_version", out var schemaElement)
                    || !schemaElement.TryGetInt32(out var schemaVersion))
                {
                    return Failure("Workflow document schema_version is missing or invalid.");
                }

                WorkflowDocumentEnvelope? envelope;
                int? migratedFrom = null;
                if (schemaVersion == CurrentSchemaVersion)
                {
                    envelope = JsonSerializer.Deserialize<WorkflowDocumentEnvelope>(json, CompactJson);
                }
                else if (schemaVersion == 1)
                {
                    var legacy = JsonSerializer.Deserialize<LegacyWorkflowDocument>(json, CompactJson);
                    envelope = legacy?.Workflow is null
                        ? null
                        : new WorkflowDocumentEnvelope(
                            CurrentSchemaVersion,
                            new WorkflowDocumentSource(WorkflowDocumentSourceKind.Imported, "legacy-v1"),
                            legacy.Workflow);
                    migratedFrom = 1;
                }
                else
                {
                    return Failure($"Workflow document schema version is not supported: {schemaVersion}");
                }

                if (envelope?.Workflow is null || envelope.Source is null)
                    return Failure("Workflow document content is incomplete.");
                if (!IsSourceId(envelope.Source.SourceId))
                    return Failure("Workflow document source_id is invalid.");

                var normalized = Normalize(envelope.Workflow);
                var validationError = ValidateStructure(normalized);
                if (validationError is not null) return Failure(validationError);
                var definitionHash = ComputeDefinitionSha256(normalized);
                var policy = trustPolicy ?? WorkflowSourceTrustPolicy.Empty;
                var trustLevel = isManagedFile
                    && envelope.Source.Kind == WorkflowDocumentSourceKind.LocalManaged
                        ? WorkflowDocumentTrustLevel.LocalManaged
                        : policy.IsTrusted(envelope.Source.SourceId, definitionHash)
                            ? WorkflowDocumentTrustLevel.TrustedSource
                            : WorkflowDocumentTrustLevel.Untrusted;

                return new WorkflowDocumentReadResult(
                    true,
                    normalized,
                    envelope.Source,
                    trustLevel,
                    definitionHash,
                    migratedFrom,
                    null);
            }
            catch (JsonException ex)
            {
                return Failure($"Workflow document JSON is invalid: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                return Failure(ex.Message);
            }
        }

        public static string ComputeDefinitionSha256(CommandWorkflowDefinition workflow)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Normalize(workflow), CompactJson);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        public static bool ContainsSensitiveInputs(CommandWorkflowDefinition workflow)
        {
            ArgumentNullException.ThrowIfNull(workflow);
            return workflow.Steps.Any(step =>
                ContainsSensitiveInputs(step.Command?.Invocation)
                || ContainsSensitiveInputs(step.Compensation?.Invocation));
        }

        private static bool ContainsSensitiveInputs(PluginCommandInvocation? invocation)
            => invocation is not null
                && (!string.IsNullOrEmpty(invocation.Text)
                    || (invocation.Paths?.Count ?? 0) > 0
                    || invocation.ImagePng is { Length: > 0 }
                    || (invocation.Arguments?.Count ?? 0) > 0);

        private static CommandWorkflowDefinition Normalize(CommandWorkflowDefinition workflow)
            => new(
                workflow.Id?.Trim() ?? string.Empty,
                workflow.Name?.Trim() ?? string.Empty,
                workflow.FailureMode,
                (workflow.Steps ?? Array.Empty<CommandWorkflowStep>())
                    .Select(step => new CommandWorkflowStep(
                        step.Id?.Trim() ?? string.Empty,
                        step.Effect,
                        Normalize(step.Command),
                        Normalize(step.Compensation)))
                    .ToList());

        private static WorkflowCommand? Normalize(WorkflowCommand? command)
        {
            if (command is null) return null;
            var invocation = command.Invocation;
            return new WorkflowCommand(
                command.CommandKey?.Trim() ?? string.Empty,
                invocation is null
                    ? null
                    : new PluginCommandInvocation
                    {
                        CommandId = invocation.CommandId?.Trim() ?? string.Empty,
                        InputType = invocation.InputType,
                        Text = invocation.Text,
                        Paths = invocation.Paths?.ToArray() ?? Array.Empty<string>(),
                        ImagePng = invocation.ImagePng?.ToArray(),
                        Arguments = NormalizeArguments(invocation.Arguments),
                    });
        }

        private static IReadOnlyDictionary<string, string> NormalizeArguments(
            IReadOnlyDictionary<string, string>? arguments)
        {
            var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (arguments is null) return normalized;
            foreach (var argument in arguments) normalized[argument.Key] = argument.Value;
            return normalized;
        }

        private static string? ValidateStructure(CommandWorkflowDefinition workflow)
        {
            if (!IsIdentifier(workflow.Id)) return "Workflow id is invalid.";
            if (string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Length > 128)
                return "Workflow name must contain between 1 and 128 characters.";
            if (!Enum.IsDefined(workflow.FailureMode)) return "Workflow failure mode is invalid.";
            if (workflow.Steps.Count is < 1 or > CommandWorkflowPlanner.MaximumStepCount)
                return $"Workflow must contain between 1 and {CommandWorkflowPlanner.MaximumStepCount} steps.";
            var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in workflow.Steps)
            {
                if (!IsIdentifier(step.Id) || !stepIds.Add(step.Id))
                    return "Workflow step ids must be valid and unique.";
                if (!Enum.IsDefined(step.Effect)) return $"Workflow step effect is invalid: {step.Id}";
                var commandError = ValidateCommandStructure(step.Command, step.Id, "command");
                if (commandError is not null) return commandError;
                if (step.Compensation is not null)
                {
                    var compensationError = ValidateCommandStructure(
                        step.Compensation,
                        step.Id,
                        "compensation");
                    if (compensationError is not null) return compensationError;
                }
                if (workflow.FailureMode == WorkflowFailureMode.Compensate
                    && step.Effect == WorkflowStepEffect.Mutating
                    && step.Compensation is null)
                {
                    return $"Mutating workflow step requires compensation: {step.Id}";
                }
            }
            return null;
        }

        private static string? ValidateCommandStructure(
            WorkflowCommand? command,
            string stepId,
            string role)
        {
            if (command?.Invocation is null
                || string.IsNullOrWhiteSpace(command.CommandKey)
                || command.CommandKey.Length > 160
                || string.IsNullOrWhiteSpace(command.Invocation.CommandId)
                || command.Invocation.CommandId.Length > 128)
            {
                return $"Workflow step {role} is incomplete: {stepId}";
            }
            var invocation = command.Invocation;
            if (!Enum.IsDefined(invocation.InputType))
                return $"Workflow step {role} input type is invalid: {stepId}";
            if ((invocation.Text?.Length ?? 0) > 65536)
                return $"Workflow step {role} text is too large: {stepId}";
            if ((invocation.Paths?.Count ?? 0) > 64
                || (invocation.Paths?.Any(path => path is null || path.Length > 32768) ?? false))
            {
                return $"Workflow step {role} paths are invalid: {stepId}";
            }
            if ((invocation.ImagePng?.Length ?? 0) > MaximumImageBytes)
                return $"Workflow step {role} image is too large: {stepId}";
            if ((invocation.Arguments?.Count ?? 0) > 64
                || (invocation.Arguments?.Any(argument =>
                    string.IsNullOrWhiteSpace(argument.Key)
                    || argument.Key.Length > 128
                    || argument.Value is null
                    || argument.Value.Length > 65536) ?? false))
            {
                return $"Workflow step {role} arguments are invalid: {stepId}";
            }
            return null;
        }

        private static bool IsIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 64
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

        private static bool IsSourceId(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 128
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-' or ':');

        private static WorkflowDocumentReadResult Failure(string error)
            => new(false, null, null, WorkflowDocumentTrustLevel.Untrusted, string.Empty, null, error);

        private static JsonSerializerOptions CreateOptions(bool writeIndented)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = false,
                WriteIndented = writeIndented,
                MaxDepth = 32,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
            return options;
        }

        private sealed record LegacyWorkflowDocument(
            int SchemaVersion,
            CommandWorkflowDefinition Workflow);
    }
}
