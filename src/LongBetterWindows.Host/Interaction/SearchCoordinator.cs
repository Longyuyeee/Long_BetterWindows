using System.Diagnostics;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class SearchCoordinator
    {
        private readonly Dictionary<string, ISearchProvider> _providers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private readonly TimeSpan _providerTimeout;
        private readonly SearchPreferenceService? _preferences;
        private readonly Func<string, bool>? _commandEnabled;

        public SearchCoordinator(
            IEnumerable<ISearchProvider> providers,
            TimeSpan? providerTimeout = null,
            SearchPreferenceService? preferences = null,
            Func<string, bool>? commandEnabled = null)
        {
            _providerTimeout = providerTimeout ?? TimeSpan.FromMilliseconds(900);
            _preferences = preferences;
            _commandEnabled = commandEnabled;
            foreach (var provider in providers)
                RegisterProvider(provider);
        }

        public void RegisterProvider(ISearchProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            lock (_lock) _providers[provider.Id] = provider;
        }

        public void UnregisterProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return;
            lock (_lock) _providers.Remove(providerId);
        }

        public async Task<IReadOnlyList<SearchResultItem>> SearchIncrementalAsync(
            SearchRequest request,
            Func<IReadOnlyList<SearchResultItem>, Task>? resultsUpdated = null,
            CancellationToken cancellationToken = default,
            Action<SearchRunMetrics>? metricsCompleted = null)
        {
            var started = Stopwatch.GetTimestamp();
            if (request.MaxResults <= 0)
            {
                metricsCompleted?.Invoke(new SearchRunMetrics(
                    null,
                    Stopwatch.GetElapsedTime(started),
                    0,
                    0,
                    0));
                return Array.Empty<SearchResultItem>();
            }

            if (_preferences is not null)
            {
                request = request with
                {
                    PinnedResultIds = _preferences.GetPinnedResultIds(),
                    RecentResultIds = _preferences.GetRecentResultIds(),
                };
            }

            List<ISearchProvider> providers;
            lock (_lock)
                providers = _providers.Values
                    .OrderByDescending(provider => provider.Priority)
                    .ToList();

            var pending = providers
                .Select(provider => CaptureProviderAsync(provider, request, cancellationToken))
                .ToList();
            var merged = new Dictionary<string, SearchResultItem>(
                StringComparer.OrdinalIgnoreCase);
            TimeSpan? firstBatchElapsed = null;
            var batchCount = 0;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var batch = await completed;
                foreach (var item in batch)
                {
                    if (!merged.TryGetValue(item.Id, out var existing)
                        || item.Score > existing.Score)
                    {
                        merged[item.Id] = item;
                    }
                }

                var snapshot = Order(
                    merged.Values,
                    request.MaxResults,
                    !string.IsNullOrWhiteSpace(request.Query));
                batchCount++;
                if (firstBatchElapsed is null && snapshot.Count > 0)
                    firstBatchElapsed = Stopwatch.GetElapsedTime(started);
                if (resultsUpdated is not null)
                    await resultsUpdated(snapshot);
            }

            var final = Order(
                merged.Values,
                request.MaxResults,
                !string.IsNullOrWhiteSpace(request.Query));
            metricsCompleted?.Invoke(new SearchRunMetrics(
                firstBatchElapsed,
                Stopwatch.GetElapsedTime(started),
                providers.Count,
                batchCount,
                final.Count));
            return final;
        }

        private async Task<IReadOnlyList<SearchResultItem>> CaptureProviderAsync(
            ISearchProvider provider,
            SearchRequest request,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_providerTimeout);
            try
            {
                var results = await provider.SearchAsync(request, timeout.Token)
                    .WaitAsync(timeout.Token);
                return results
                    .Where(item => IsActionEnabled(item.PrimaryAction))
                    .Select(item => item with
                    {
                        ProviderId = provider.Id,
                        ProviderPriority = provider.Priority,
                        SecondaryActions = item.SecondaryActions
                            .Where(IsActionEnabled)
                            .ToList(),
                    })
                    .ToList();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Debug("搜索 Provider 超时: {ProviderId}", provider.Id);
                return Array.Empty<SearchResultItem>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "搜索 Provider 失败: {ProviderId}", provider.Id);
                return Array.Empty<SearchResultItem>();
            }
        }

        private bool IsActionEnabled(SearchResultAction action)
            => action.Kind != SearchActionKind.ExecuteCommand
                || _commandEnabled?.Invoke(action.Target) != false;

        private IReadOnlyList<SearchResultItem> Order(
            IEnumerable<SearchResultItem> results,
            int maxResults,
            bool hasActiveQuery)
        {
            var now = DateTimeOffset.UtcNow;
            return results
                .Select(item =>
                {
                    var preference = _preferences?.GetScore(
                            item.Id,
                            now,
                            hasActiveQuery)
                        ?? new SearchPreferenceScore(false, 0);
                    return item with
                    {
                        IsPinned = preference.IsPinned,
                        PreferenceScore = preference.Score,
                    };
                })
                .OrderByDescending(item => item.Score + item.PreferenceScore)
                .ThenByDescending(item => item.ProviderPriority)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }
    }
}
