namespace LongBetterWindows.Host.Interaction
{
    internal sealed class WorkspaceSessionCoordinator
    {
        private readonly object _stateLock = new();
        private readonly WorkspaceModuleKey _rootKey;
        private readonly List<WorkspaceModuleDescriptor> _modules;
        private readonly List<WorkspaceModuleKey> _activationOrder;
        private WorkspaceModuleKey _activeKey;

        public WorkspaceSessionCoordinator(WorkspaceModuleDescriptor rootModule)
        {
            ArgumentNullException.ThrowIfNull(rootModule);
            if (rootModule.CanClose)
                throw new ArgumentException(
                    "The workspace root module must not be closeable.",
                    nameof(rootModule));

            _rootKey = rootModule.Key;
            _modules = [rootModule];
            _activationOrder = [rootModule.Key];
            _activeKey = rootModule.Key;
        }

        public event EventHandler<WorkspaceNavigationChangedEventArgs>? StateChanged;

        public WorkspaceNavigationState State
        {
            get
            {
                lock (_stateLock)
                    return CreateState();
            }
        }

        public WorkspaceNavigationResult Open(
            Func<WorkspaceModuleDescriptor> moduleFactory)
        {
            ArgumentNullException.ThrowIfNull(moduleFactory);
            var module = moduleFactory();
            ArgumentNullException.ThrowIfNull(module);
            return Open(module);
        }

        public WorkspaceNavigationResult Open(WorkspaceModuleDescriptor module)
        {
            ArgumentNullException.ThrowIfNull(module);

            WorkspaceNavigationChangedEventArgs? change = null;
            WorkspaceNavigationResult result;
            lock (_stateLock)
            {
                var existing = FindModule(module.Key);
                if (existing is not null)
                {
                    result = ActivateCore(existing.Key, out change);
                }
                else
                {
                    var previous = CreateState();
                    _modules.Add(module);
                    SetMostRecent(module.Key);
                    _activeKey = module.Key;
                    var current = CreateState();
                    change = new WorkspaceNavigationChangedEventArgs(
                        WorkspaceNavigationChangeKind.Opened,
                        previous,
                        current,
                        affectedCount: 1);
                    result = new WorkspaceNavigationResult(
                        WorkspaceNavigationChangeKind.Opened,
                        current,
                        AffectedCount: 1);
                }
            }

            RaiseStateChanged(change);
            return result;
        }

        public WorkspaceNavigationResult Activate(WorkspaceModuleKey key)
        {
            WorkspaceNavigationChangedEventArgs? change;
            WorkspaceNavigationResult result;
            lock (_stateLock)
            {
                var existing = FindModule(key);
                if (existing is null)
                {
                    return new WorkspaceNavigationResult(
                        WorkspaceNavigationChangeKind.NotFound,
                        CreateState());
                }

                result = ActivateCore(existing.Key, out change);
            }

            RaiseStateChanged(change);
            return result;
        }

        public WorkspaceNavigationResult Close(WorkspaceModuleKey key)
        {
            WorkspaceNavigationChangedEventArgs? change = null;
            WorkspaceNavigationResult result;
            lock (_stateLock)
            {
                var module = FindModule(key);
                if (module is null)
                {
                    return new WorkspaceNavigationResult(
                        WorkspaceNavigationChangeKind.NotFound,
                        CreateState());
                }

                if (module.Key == _rootKey || !module.CanClose)
                {
                    return new WorkspaceNavigationResult(
                        WorkspaceNavigationChangeKind.Protected,
                        CreateState());
                }

                var previous = CreateState();
                var wasActive = module.Key == _activeKey;
                _modules.Remove(module);
                _activationOrder.RemoveAll(candidate => candidate == module.Key);
                if (wasActive)
                    _activeKey = GetMostRecentValidKey();

                var current = CreateState();
                change = new WorkspaceNavigationChangedEventArgs(
                    WorkspaceNavigationChangeKind.Closed,
                    previous,
                    current,
                    affectedCount: 1);
                result = new WorkspaceNavigationResult(
                    WorkspaceNavigationChangeKind.Closed,
                    current,
                    AffectedCount: 1);
            }

            RaiseStateChanged(change);
            return result;
        }

        public WorkspaceNavigationResult RemoveModules(
            IEnumerable<WorkspaceModuleKey> keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            var keysToRemove = keys
                .Where(key => key != _rootKey)
                .ToHashSet();

            if (keysToRemove.Count == 0)
            {
                return new WorkspaceNavigationResult(
                    WorkspaceNavigationChangeKind.None,
                    State);
            }

            WorkspaceNavigationChangedEventArgs? change = null;
            WorkspaceNavigationResult result;
            lock (_stateLock)
            {
                var removable = _modules
                    .Where(module => keysToRemove.Contains(module.Key))
                    .ToArray();
                if (removable.Length == 0)
                {
                    return new WorkspaceNavigationResult(
                        WorkspaceNavigationChangeKind.NotFound,
                        CreateState());
                }

                var previous = CreateState();
                var removedKeys = removable
                    .Select(module => module.Key)
                    .ToHashSet();
                _modules.RemoveAll(module => removedKeys.Contains(module.Key));
                _activationOrder.RemoveAll(removedKeys.Contains);
                if (removedKeys.Contains(_activeKey))
                    _activeKey = GetMostRecentValidKey();

                var current = CreateState();
                change = new WorkspaceNavigationChangedEventArgs(
                    WorkspaceNavigationChangeKind.Removed,
                    previous,
                    current,
                    removable.Length);
                result = new WorkspaceNavigationResult(
                    WorkspaceNavigationChangeKind.Removed,
                    current,
                    removable.Length);
            }

            RaiseStateChanged(change);
            return result;
        }

        private WorkspaceNavigationResult ActivateCore(
            WorkspaceModuleKey key,
            out WorkspaceNavigationChangedEventArgs? change)
        {
            change = null;
            if (_activeKey == key)
            {
                return new WorkspaceNavigationResult(
                    WorkspaceNavigationChangeKind.None,
                    CreateState());
            }

            var previous = CreateState();
            _activeKey = key;
            SetMostRecent(key);
            var current = CreateState();
            change = new WorkspaceNavigationChangedEventArgs(
                WorkspaceNavigationChangeKind.Activated,
                previous,
                current,
                affectedCount: 1);
            return new WorkspaceNavigationResult(
                WorkspaceNavigationChangeKind.Activated,
                current,
                AffectedCount: 1);
        }

        private WorkspaceModuleDescriptor? FindModule(WorkspaceModuleKey key)
            => _modules.FirstOrDefault(module => module.Key == key);

        private void SetMostRecent(WorkspaceModuleKey key)
        {
            _activationOrder.RemoveAll(candidate => candidate == key);
            _activationOrder.Add(key);
        }

        private WorkspaceModuleKey GetMostRecentValidKey()
        {
            for (var index = _activationOrder.Count - 1; index >= 0; index--)
            {
                var candidate = _activationOrder[index];
                if (_modules.Any(module => module.Key == candidate))
                    return candidate;
            }

            return _rootKey;
        }

        private WorkspaceNavigationState CreateState()
            => new(_modules, _activeKey);

        private void RaiseStateChanged(WorkspaceNavigationChangedEventArgs? change)
        {
            if (change is not null)
                StateChanged?.Invoke(this, change);
        }
    }
}
