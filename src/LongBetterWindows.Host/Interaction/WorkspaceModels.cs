namespace LongBetterWindows.Host.Interaction
{
    internal readonly record struct WorkspaceModuleKey
    {
        public WorkspaceModuleKey(
            string kind,
            string resourceId,
            string? instanceId = null)
        {
            Kind = NormalizeRequired(kind, nameof(kind));
            ResourceId = NormalizeRequired(resourceId, nameof(resourceId));
            InstanceId = string.IsNullOrWhiteSpace(instanceId)
                ? null
                : instanceId.Trim().ToLowerInvariant();
        }

        public string Kind { get; }
        public string ResourceId { get; }
        public string? InstanceId { get; }

        public override string ToString()
            => InstanceId is null
                ? $"{Kind}:{ResourceId}"
                : $"{Kind}:{ResourceId}:{InstanceId}";

        private static string NormalizeRequired(string value, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            return value.Trim().ToLowerInvariant();
        }
    }

    internal sealed record WorkspaceModuleDescriptor
    {
        public WorkspaceModuleDescriptor(
            WorkspaceModuleKey key,
            string title,
            bool canClose = true,
            bool supportsDetach = false,
            string? searchScopeId = null)
        {
            if (string.IsNullOrWhiteSpace(key.Kind)
                || string.IsNullOrWhiteSpace(key.ResourceId))
            {
                throw new ArgumentException(
                    "Workspace module keys must be created with a kind and resource ID.",
                    nameof(key));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            Key = key;
            Title = title.Trim();
            CanClose = canClose;
            SupportsDetach = supportsDetach;
            SearchScopeId = string.IsNullOrWhiteSpace(searchScopeId)
                ? null
                : searchScopeId.Trim();
        }

        public WorkspaceModuleKey Key { get; }
        public string Title { get; }
        public bool CanClose { get; }
        public bool SupportsDetach { get; }
        public string? SearchScopeId { get; }
    }

    internal sealed class WorkspaceNavigationState
    {
        private readonly WorkspaceModuleDescriptor[] _modules;

        internal WorkspaceNavigationState(
            IEnumerable<WorkspaceModuleDescriptor> modules,
            WorkspaceModuleKey activeModuleKey)
        {
            _modules = modules.ToArray();
            Modules = Array.AsReadOnly(_modules);
            ActiveModuleKey = activeModuleKey;
        }

        public IReadOnlyList<WorkspaceModuleDescriptor> Modules { get; }
        public WorkspaceModuleKey ActiveModuleKey { get; }

        public WorkspaceModuleDescriptor ActiveModule
            => _modules.First(module => module.Key == ActiveModuleKey);

        public bool Contains(WorkspaceModuleKey key)
            => _modules.Any(module => module.Key == key);
    }

    internal enum WorkspaceNavigationChangeKind
    {
        None,
        Opened,
        Activated,
        Closed,
        Removed,
        NotFound,
        Protected,
    }

    internal sealed record WorkspaceNavigationResult(
        WorkspaceNavigationChangeKind Kind,
        WorkspaceNavigationState State,
        int AffectedCount = 0)
    {
        public bool Changed => Kind is
            WorkspaceNavigationChangeKind.Opened
            or WorkspaceNavigationChangeKind.Activated
            or WorkspaceNavigationChangeKind.Closed
            or WorkspaceNavigationChangeKind.Removed;
    }

    internal sealed class WorkspaceNavigationChangedEventArgs : EventArgs
    {
        public WorkspaceNavigationChangedEventArgs(
            WorkspaceNavigationChangeKind kind,
            WorkspaceNavigationState previous,
            WorkspaceNavigationState current,
            int affectedCount)
        {
            Kind = kind;
            Previous = previous;
            Current = current;
            AffectedCount = affectedCount;
        }

        public WorkspaceNavigationChangeKind Kind { get; }
        public WorkspaceNavigationState Previous { get; }
        public WorkspaceNavigationState Current { get; }
        public int AffectedCount { get; }
    }
}
