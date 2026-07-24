using System.IO;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record CommandWorkflowTemplateSummary(
        string Key,
        string Id,
        string Name,
        WorkflowFailureMode FailureMode,
        int StepCount,
        WorkflowDocumentSource Source,
        WorkflowDocumentTrustLevel TrustLevel,
        string DefinitionSha256,
        bool ContainsSensitiveInputs);

    public sealed record CommandWorkflowTemplateIssue(
        string FileName,
        string Error);

    public sealed record CommandWorkflowTemplateListResult(
        bool IsSuccess,
        IReadOnlyList<CommandWorkflowTemplateSummary> Templates,
        IReadOnlyList<CommandWorkflowTemplateIssue> Issues,
        string? Error);

    /// <summary>Reads bounded workflow templates without creating or modifying the catalog directory.</summary>
    public sealed class CommandWorkflowTemplateCatalog
    {
        public const int MaximumTemplateCount = 128;

        private readonly string _root;
        private readonly CommandWorkflowRepository _repository;

        public CommandWorkflowTemplateCatalog(
            string rootDirectory,
            CommandWorkflowRepository repository)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Workflow template root must not be empty.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<CommandWorkflowTemplateListResult> ListAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var root = GetExistingRoot();
                if (root is null)
                {
                    return new CommandWorkflowTemplateListResult(
                        true,
                        Array.Empty<CommandWorkflowTemplateSummary>(),
                        Array.Empty<CommandWorkflowTemplateIssue>(),
                        null);
                }

                var paths = Directory.EnumerateFiles(
                        root.FullName,
                        "*.workflow.json",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumTemplateCount + 1)
                    .ToList();
                if (paths.Count > MaximumTemplateCount)
                {
                    return Failure(
                        $"Workflow template catalog cannot contain more than {MaximumTemplateCount} templates.");
                }

                var templates = new List<CommandWorkflowTemplateSummary>();
                var issues = new List<CommandWorkflowTemplateIssue>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureRootIsStable(root.FullName);
                    var result = await _repository.ImportAsync(path, cancellationToken);
                    var fileName = Path.GetFileName(path);
                    if (!result.IsSuccess)
                    {
                        issues.Add(new CommandWorkflowTemplateIssue(
                            fileName,
                            result.Error ?? "Workflow template could not be read."));
                        continue;
                    }

                    var workflow = result.Workflow!;
                    if (!ids.Add(workflow.Id))
                    {
                        issues.Add(new CommandWorkflowTemplateIssue(
                            fileName,
                            $"Workflow template id is duplicated: {workflow.Id}"));
                        continue;
                    }
                    templates.Add(new CommandWorkflowTemplateSummary(
                        fileName,
                        workflow.Id,
                        workflow.Name,
                        workflow.FailureMode,
                        workflow.Steps.Count,
                        result.Source!,
                        result.TrustLevel,
                        result.DefinitionSha256,
                        CommandWorkflowDocumentCodec.ContainsSensitiveInputs(workflow)));
                }

                return new CommandWorkflowTemplateListResult(
                    true,
                    templates
                        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    issues,
                    null);
            }
            catch (Exception ex) when (
                ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return Failure($"Workflow template catalog is unavailable: {ex.Message}");
            }
        }

        public async Task<WorkflowDocumentReadResult> OpenAsync(
            string templateKey,
            string expectedDefinitionSha256,
            CancellationToken cancellationToken = default)
        {
            if (!IsTemplateKey(templateKey))
                return ReadFailure("Workflow template key is invalid.");
            if (!IsSha256(expectedDefinitionSha256))
                return ReadFailure("Expected workflow template hash must be 64 hexadecimal characters.");

            try
            {
                var root = GetExistingRoot();
                if (root is null) return ReadFailure("Workflow template catalog was not found.");
                var path = Path.GetFullPath(Path.Combine(root.FullName, templateKey));
                if (!IsWithinRoot(path))
                    return ReadFailure("Workflow template path escapes the catalog root.");
                EnsureRootIsStable(root.FullName);
                var result = await _repository.ImportAsync(path, cancellationToken);
                if (!result.IsSuccess) return result;
                if (!string.Equals(
                    result.DefinitionSha256,
                    expectedDefinitionSha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return ReadFailure("Workflow template changed after it was listed; refresh the catalog.");
                }
                return result;
            }
            catch (Exception ex) when (
                ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return ReadFailure($"Workflow template could not be opened: {ex.Message}");
            }
        }

        private DirectoryInfo? GetExistingRoot()
        {
            var root = new DirectoryInfo(_root);
            if (!root.Exists) return null;
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Workflow template root must not be a reparse point.");
            return root;
        }

        private void EnsureRootIsStable(string expectedPath)
        {
            var root = GetExistingRoot()
                ?? throw new IOException("Workflow template catalog was removed.");
            if (!string.Equals(root.FullName, expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Workflow template catalog path changed.");
        }

        private bool IsWithinRoot(string path)
        {
            var prefix = _root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTemplateKey(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 160
                && string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
                && value.EndsWith(".workflow.json", StringComparison.OrdinalIgnoreCase);

        private static bool IsSha256(string value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
        }

        private static CommandWorkflowTemplateListResult Failure(string error)
            => new(
                false,
                Array.Empty<CommandWorkflowTemplateSummary>(),
                Array.Empty<CommandWorkflowTemplateIssue>(),
                error);

        private static WorkflowDocumentReadResult ReadFailure(string error)
            => new(
                false,
                null,
                null,
                WorkflowDocumentTrustLevel.Untrusted,
                string.Empty,
                null,
                WorkflowErrorCode.ValidationFailed,
                error);
    }
}
