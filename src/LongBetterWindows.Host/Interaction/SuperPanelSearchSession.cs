using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class SuperPanelSearchSession : IDisposable
    {
        private readonly ContextCaptureService _contextCapture;
        private readonly SearchCoordinator _search;
        private readonly SuperPanelGroupService _groups;
        private CancellationTokenSource? _captureCts;
        private CancellationTokenSource? _searchCts;
        private bool _disposed;

        public SuperPanelSearchSession(
            ContextCaptureService contextCapture,
            SearchCoordinator search,
            SuperPanelGroupService groups)
        {
            _contextCapture = contextCapture;
            _search = search;
            _groups = groups;
        }

        public ContextSnapshot CurrentContext { get; private set; } = ContextSnapshot.Empty;

        public event EventHandler<SuperPanelContextUpdate>? ContextUpdated;
        public event EventHandler<SuperPanelResultsUpdate>? ResultsUpdated;

        public async Task StartCaptureAsync(ContextCaptureRequest request)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCapture();
            _captureCts = new CancellationTokenSource();
            var token = _captureCts.Token;

            CurrentContext = ContextSnapshot.Empty;
            ContextUpdated?.Invoke(
                this, new SuperPanelContextUpdate(CurrentContext, IsLoading: true));
            ResultsUpdated?.Invoke(
                this, new SuperPanelResultsUpdate(
                    Array.Empty<SearchResultItem>(), Completed: false));
            _ = RestartSearchAsync();

            try
            {
                var snapshot = await _contextCapture.CaptureAsync(request, token);
                token.ThrowIfCancellationRequested();
                await ApplyContextAsync(snapshot, token);
            }
            catch (OperationCanceledException)
            {
                // A newer panel invocation superseded this capture.
            }
        }

        public async Task StartWithContextAsync(ContextSnapshot snapshot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCapture();
            _captureCts = new CancellationTokenSource();
            await ApplyContextAsync(snapshot, _captureCts.Token);
        }

        public Task RefreshSearchAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RestartSearchAsync();
        }

        public void Cancel()
        {
            CancelCapture();
            CancelSearch();
        }

        private async Task ApplyContextAsync(
            ContextSnapshot snapshot,
            CancellationToken captureToken)
        {
            captureToken.ThrowIfCancellationRequested();
            CurrentContext = snapshot;
            ContextUpdated?.Invoke(
                this, new SuperPanelContextUpdate(snapshot, IsLoading: false));
            ResultsUpdated?.Invoke(
                this, new SuperPanelResultsUpdate(
                    Array.Empty<SearchResultItem>(), Completed: false));
            await RestartSearchAsync();
        }

        private Task RestartSearchAsync()
        {
            CancelSearch();
            _searchCts = new CancellationTokenSource();
            return RunSearchAsync(CurrentContext, _searchCts.Token);
        }

        private async Task RunSearchAsync(
            ContextSnapshot context,
            CancellationToken cancellationToken)
        {
            try
            {
                var groupedResultIds = _groups.GetGroups()
                    .SelectMany(group => group.ResultIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var results = await _search.SearchIncrementalAsync(
                    new SearchRequest(
                        string.Empty,
                        context,
                        24,
                        AdditionalPreferredResultIds: groupedResultIds),
                    snapshot =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            ResultsUpdated?.Invoke(
                                this,
                                new SuperPanelResultsUpdate(snapshot, Completed: false));
                        return Task.CompletedTask;
                    },
                    cancellationToken,
                    metrics => Log.Debug(
                        "Super Panel search completed: FirstBatchMs={FirstBatchMs:F1}, TotalMs={TotalMs:F1}, Providers={ProviderCount}, Batches={BatchCount}, Results={ResultCount}",
                        metrics.FirstBatchElapsed?.TotalMilliseconds,
                        metrics.TotalElapsed.TotalMilliseconds,
                        metrics.ProviderCount,
                        metrics.BatchCount,
                        metrics.ResultCount));
                cancellationToken.ThrowIfCancellationRequested();
                ResultsUpdated?.Invoke(
                    this, new SuperPanelResultsUpdate(results, Completed: true));
            }
            catch (OperationCanceledException)
            {
                // A newer context or plugin set superseded this search.
            }
        }

        private void CancelCapture()
        {
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = null;
        }

        private void CancelSearch()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
        }
    }

    internal sealed record SuperPanelContextUpdate(
        ContextSnapshot Snapshot,
        bool IsLoading);

    internal sealed record SuperPanelResultsUpdate(
        IReadOnlyList<SearchResultItem> Results,
        bool Completed);
}
