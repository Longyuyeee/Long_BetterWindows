namespace LongBetterWindows.Host.Interaction
{
    internal enum PluginWorkspacePlacement
    {
        Hidden,
        Embedded,
        Detached,
        Ended,
    }

    internal enum PluginSurfaceCloseAction
    {
        Ignore,
        ReturnToEmbedded,
        HideAndApplyLifecycle,
    }

    internal static class PluginSurfaceCloseRouter
    {
        public static PluginSurfaceCloseAction Route(
            bool closingForStop,
            bool returnRequested,
            bool canEmbed)
        {
            if (closingForStop)
                return PluginSurfaceCloseAction.Ignore;
            if (returnRequested && canEmbed)
                return PluginSurfaceCloseAction.ReturnToEmbedded;
            return PluginSurfaceCloseAction.HideAndApplyLifecycle;
        }
    }

    internal sealed record PluginWorkspaceSessionState(
        string SessionId,
        string PluginId,
        PluginWorkspacePlacement Placement,
        PluginWorkspacePlacement LastVisiblePlacement,
        long Revision)
    {
        public bool IsVisible => Placement is
            PluginWorkspacePlacement.Embedded
            or PluginWorkspacePlacement.Detached;
        public bool IsEnded => Placement == PluginWorkspacePlacement.Ended;
    }

    internal sealed record PluginWorkspaceTransition(
        PluginWorkspaceSessionState Previous,
        PluginWorkspaceSessionState Current)
    {
        public bool Changed => Previous != Current;
    }

    internal sealed class PluginWorkspaceSession
    {
        private readonly object _stateLock = new();
        private PluginWorkspaceSessionState _state;

        internal PluginWorkspaceSession(
            string sessionId,
            string pluginId,
            PluginWorkspacePlacement preferredPlacement)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            if (preferredPlacement is not
                (PluginWorkspacePlacement.Embedded
                    or PluginWorkspacePlacement.Detached))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(preferredPlacement),
                    "The preferred placement must be visible.");
            }

            _state = new PluginWorkspaceSessionState(
                sessionId.Trim().ToLowerInvariant(),
                pluginId.Trim().ToLowerInvariant(),
                PluginWorkspacePlacement.Hidden,
                preferredPlacement,
                Revision: 0);
        }

        public PluginWorkspaceSessionState State
        {
            get
            {
                lock (_stateLock)
                    return _state;
            }
        }

        public PluginWorkspaceTransition ShowEmbedded()
            => MoveTo(PluginWorkspacePlacement.Embedded);

        public PluginWorkspaceTransition ShowDetached()
            => MoveTo(PluginWorkspacePlacement.Detached);

        public PluginWorkspaceTransition Hide()
            => MoveTo(PluginWorkspacePlacement.Hidden);

        public PluginWorkspaceTransition End()
            => MoveTo(PluginWorkspacePlacement.Ended);

        private PluginWorkspaceTransition MoveTo(
            PluginWorkspacePlacement placement)
        {
            lock (_stateLock)
            {
                var previous = _state;
                if (previous.IsEnded || previous.Placement == placement)
                    return new PluginWorkspaceTransition(previous, previous);
                if (placement == PluginWorkspacePlacement.Hidden
                    && !previous.IsVisible)
                {
                    return new PluginWorkspaceTransition(previous, previous);
                }

                var lastVisible = placement is
                    PluginWorkspacePlacement.Embedded
                    or PluginWorkspacePlacement.Detached
                        ? placement
                        : previous.LastVisiblePlacement;
                _state = previous with
                {
                    Placement = placement,
                    LastVisiblePlacement = lastVisible,
                    Revision = previous.Revision + 1,
                };
                return new PluginWorkspaceTransition(previous, _state);
            }
        }
    }

    internal sealed class PluginWorkspaceSessionManager
    {
        private readonly object _stateLock = new();
        private readonly Func<string> _sessionIdFactory;
        private readonly Dictionary<string, PluginWorkspaceSession> _byPlugin =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginWorkspaceSession> _bySession =
            new(StringComparer.OrdinalIgnoreCase);

        public PluginWorkspaceSessionManager(
            Func<string>? sessionIdFactory = null)
        {
            _sessionIdFactory = sessionIdFactory
                ?? (() => Guid.NewGuid().ToString("N"));
        }

        public PluginWorkspaceSession GetOrCreate(
            string pluginId,
            PluginWorkspacePlacement preferredPlacement)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            lock (_stateLock)
            {
                if (_byPlugin.TryGetValue(pluginId, out var existing)
                    && !existing.State.IsEnded)
                {
                    return existing;
                }

                var sessionId = _sessionIdFactory();
                ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
                if (_bySession.ContainsKey(sessionId))
                {
                    throw new InvalidOperationException(
                        "The plugin session ID factory returned a duplicate ID.");
                }

                var created = new PluginWorkspaceSession(
                    sessionId,
                    pluginId,
                    preferredPlacement);
                _byPlugin[created.State.PluginId] = created;
                _bySession[created.State.SessionId] = created;
                return created;
            }
        }

        public PluginWorkspaceSession? GetBySessionId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;
            lock (_stateLock)
                return _bySession.GetValueOrDefault(sessionId);
        }

        public bool End(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;
            lock (_stateLock)
            {
                if (!_bySession.Remove(sessionId, out var session))
                    return false;
                session.End();
                _byPlugin.Remove(session.State.PluginId);
                return true;
            }
        }
    }
}
