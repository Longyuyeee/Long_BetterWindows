using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    /// <summary>
    /// Publishes only locally managed workflows that still pass preflight against the
    /// current plugin catalog. The workflow is loaded and checked again when opened.
    /// </summary>
    public sealed class ManagedWorkflowSearchProvider : ISearchProvider
    {
        private readonly CommandWorkflowRepository _repository;
        private readonly CommandWorkflowPlanner _planner;
        private readonly Func<string, string>? _localize;

        public ManagedWorkflowSearchProvider(
            PluginRegistry plugins,
            CommandWorkflowRepository repository,
            Func<string, string>? localize = null)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _planner = new CommandWorkflowPlanner(plugins);
            _localize = localize;
        }

        public string Id => "managed-workflows";
        public int Priority => 920;

        public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var listed = await _repository.ListManagedAsync(cancellationToken);
            if (!listed.IsSuccess || request.MaxResults <= 0)
                return Array.Empty<SearchResultItem>();

            var query = Normalize(request.Query);
            var preferredIds = query.Length == 0
                ? (request.PinnedResultIds ?? Array.Empty<string>())
                    .Concat(request.RecentResultIds ?? Array.Empty<string>())
                    .Concat(request.AdditionalPreferredResultIds ?? Array.Empty<string>())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<SearchResultItem>();
            foreach (var summary in listed.Workflows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var score = Score(summary, query);
                var resultId = "workflow:" + summary.Id;
                if (preferredIds.Contains(resultId)) score += 220;
                if (score <= 0) continue;

                var loaded = await _repository.LoadManagedAsync(summary.Id, cancellationToken);
                if (!loaded.IsSuccess || loaded.Workflow is null
                    || !string.Equals(
                        loaded.DefinitionSha256,
                        summary.DefinitionSha256,
                        StringComparison.Ordinal))
                    continue;

                var preflight = _planner.Preflight(loaded.Workflow);
                if (!preflight.IsValid) continue;

                results.Add(new SearchResultItem
                {
                    Id = resultId,
                    ProviderId = Id,
                    Title = summary.Name,
                    Subtitle = BuildSubtitle(summary, preflight),
                    Source = string.Format(
                        Text("search.workflow.source", "组合动作 · {0} 步"),
                        summary.StepCount),
                    IconKind = SearchResultIconKind.Workflow,
                    Score = score,
                    Kind = SearchResultKind.Command,
                    PrimaryAction = new SearchResultAction(
                        SearchActionKind.OpenWorkflowReview,
                        summary.Id,
                        Label: Text(
                            "search.workflow.reviewAndRun",
                            "审查并运行"),
                        ExpectedStateFingerprint: preflight.Fingerprint),
                    CanPin = true,
                });
            }

            return results
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(request.MaxResults)
                .ToList();
        }

        private static int Score(ManagedCommandWorkflowSummary workflow, string query)
        {
            if (query.Length == 0) return 160;
            var name = Normalize(workflow.Name);
            var id = Normalize(workflow.Id);
            if (name == query || id == query) return 980;
            if (name.StartsWith(query, StringComparison.Ordinal)
                || id.StartsWith(query, StringComparison.Ordinal)) return 800;
            if (name.Contains(query, StringComparison.Ordinal)
                || id.Contains(query, StringComparison.Ordinal)) return 610;
            if ("组合动作".Contains(query, StringComparison.Ordinal)
                || "工作流".Contains(query, StringComparison.Ordinal)
                || "workflow".Contains(query, StringComparison.Ordinal)) return 430;
            return 0;
        }

        private string BuildSubtitle(
            ManagedCommandWorkflowSummary workflow,
            CommandWorkflowPreflightResult preflight)
        {
            var failureMode = workflow.FailureMode == WorkflowFailureMode.Compensate
                ? Text("search.workflow.failure.compensate", "失败时回滚")
                : Text("search.workflow.failure.stop", "失败时停止");
            return string.Format(
                Text(
                    "search.workflow.subtitle",
                    "{0} · {1} 个插件 · 运行前需批准"),
                failureMode,
                preflight.Permissions.Count);
        }

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }

        private static string Normalize(string value)
            => (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
