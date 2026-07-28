namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WorkspaceSearchRequest(
        WorkspaceModuleKey ModuleKey,
        string Query,
        long Revision,
        CancellationToken CancellationToken);

    internal sealed class WorkspaceSearchSession : IDisposable
    {
        private readonly object _stateLock = new();
        private readonly Dictionary<WorkspaceModuleKey, string> _queries = new();
        private WorkspaceModuleKey? _activeModuleKey;
        private CancellationTokenSource? _activeRequest;
        private long _activeRevision;
        private bool _disposed;

        public WorkspaceModuleKey? ActiveModuleKey
        {
            get
            {
                lock (_stateLock)
                    return _activeModuleKey;
            }
        }

        public string ActivateModule(WorkspaceModuleKey key)
        {
            ThrowIfInvalid(key);
            CancellationTokenSource? previous = null;
            string query;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_activeModuleKey != key)
                {
                    previous = DetachActiveRequest();
                    _activeModuleKey = key;
                }
                query = GetQueryCore(key);
            }

            CancelAndDispose(previous);
            return query;
        }

        public bool SetQuery(WorkspaceModuleKey key, string? query)
        {
            ThrowIfInvalid(key);
            var normalized = query ?? string.Empty;
            CancellationTokenSource? previous = null;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (string.Equals(GetQueryCore(key), normalized, StringComparison.Ordinal))
                    return false;

                _queries[key] = normalized;
                if (_activeModuleKey == key)
                    previous = DetachActiveRequest();
            }

            CancelAndDispose(previous);
            return true;
        }

        public string GetQuery(WorkspaceModuleKey key)
        {
            ThrowIfInvalid(key);
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return GetQueryCore(key);
            }
        }

        public WorkspaceSearchRequest BeginSearch()
        {
            CancellationTokenSource? previous;
            WorkspaceSearchRequest request;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_activeModuleKey is not { } moduleKey)
                    throw new InvalidOperationException(
                        "A workspace module must be active before starting a search.");

                previous = DetachActiveRequest();
                _activeRequest = new CancellationTokenSource();
                _activeRevision++;
                request = new WorkspaceSearchRequest(
                    moduleKey,
                    GetQueryCore(moduleKey),
                    _activeRevision,
                    _activeRequest.Token);
            }

            CancelAndDispose(previous);
            return request;
        }

        public bool IsCurrent(WorkspaceSearchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            lock (_stateLock)
            {
                if (_disposed)
                    return false;
                return _activeModuleKey == request.ModuleKey
                    && _activeRevision == request.Revision
                    && _activeRequest is not null
                    && _activeRequest.Token == request.CancellationToken
                    && !request.CancellationToken.IsCancellationRequested;
            }
        }

        public bool Complete(WorkspaceSearchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            CancellationTokenSource? completed = null;
            lock (_stateLock)
            {
                if (_disposed)
                    return false;
                if (_activeModuleKey != request.ModuleKey
                    || _activeRevision != request.Revision
                    || _activeRequest is null
                    || _activeRequest.Token != request.CancellationToken
                    || request.CancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                completed = DetachActiveRequest();
            }

            completed?.Dispose();
            return true;
        }

        public bool RemoveModule(WorkspaceModuleKey key)
        {
            ThrowIfInvalid(key);
            CancellationTokenSource? previous = null;
            bool removed;
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                removed = _queries.Remove(key);
                if (_activeModuleKey == key)
                {
                    _activeModuleKey = null;
                    previous = DetachActiveRequest();
                    removed = true;
                }
            }

            CancelAndDispose(previous);
            return removed;
        }

        private string GetQueryCore(WorkspaceModuleKey key)
            => _queries.TryGetValue(key, out var query)
                ? query
                : string.Empty;

        private CancellationTokenSource? DetachActiveRequest()
        {
            var current = _activeRequest;
            _activeRequest = null;
            return current;
        }

        private static void CancelAndDispose(CancellationTokenSource? source)
        {
            if (source is null)
                return;
            source.Cancel();
            source.Dispose();
        }

        private static void ThrowIfInvalid(WorkspaceModuleKey key)
        {
            if (!key.IsValid)
                throw new ArgumentException(
                    "A valid workspace module key is required.",
                    nameof(key));
        }

        public void Dispose()
        {
            CancellationTokenSource? previous;
            lock (_stateLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _activeModuleKey = null;
                _queries.Clear();
                previous = DetachActiveRequest();
            }

            CancelAndDispose(previous);
        }
    }
}
