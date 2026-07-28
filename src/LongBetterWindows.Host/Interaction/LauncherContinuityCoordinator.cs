namespace LongBetterWindows.Host.Interaction
{
    internal sealed class LauncherContinuityCoordinator
    {
        private readonly object _stateLock = new();
        private PendingTransition? _pending;

        public void Begin(string workspaceTarget, LauncherReturnIntent intent)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspaceTarget);
            ArgumentNullException.ThrowIfNull(intent);
            lock (_stateLock)
            {
                _pending?.Intent.Discard();
                _pending = new PendingTransition(
                    workspaceTarget.Trim().ToLowerInvariant(),
                    intent);
            }
        }

        public bool HasPendingFor(string workspaceTarget)
        {
            if (string.IsNullOrWhiteSpace(workspaceTarget))
                return false;
            lock (_stateLock)
                return _pending is not null
                    && string.Equals(
                        _pending.WorkspaceTarget,
                        workspaceTarget.Trim(),
                        StringComparison.OrdinalIgnoreCase);
        }

        public LauncherReturnState? TryConsume(
            string workspaceTarget,
            bool originWindowIsAvailable)
        {
            if (string.IsNullOrWhiteSpace(workspaceTarget))
                return null;
            lock (_stateLock)
            {
                if (_pending is null
                    || !string.Equals(
                        _pending.WorkspaceTarget,
                        workspaceTarget.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var pending = _pending;
                _pending = null;
                return pending.Intent.Consume(originWindowIsAvailable);
            }
        }

        public void Cancel(string workspaceTarget)
        {
            if (string.IsNullOrWhiteSpace(workspaceTarget))
                return;
            lock (_stateLock)
            {
                if (_pending is null
                    || !string.Equals(
                        _pending.WorkspaceTarget,
                        workspaceTarget.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _pending.Intent.Discard();
                _pending = null;
            }
        }

        public void Discard()
        {
            lock (_stateLock)
            {
                _pending?.Intent.Discard();
                _pending = null;
            }
        }

        private sealed record PendingTransition(
            string WorkspaceTarget,
            LauncherReturnIntent Intent);
    }
}
