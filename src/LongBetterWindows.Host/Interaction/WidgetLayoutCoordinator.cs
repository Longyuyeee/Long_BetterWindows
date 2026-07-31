using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    internal enum WidgetLayoutMutationError
    {
        None,
        CatalogItemNotFound,
        MultipleInstancesNotAllowed,
        InstanceNotFound,
        SizeOutOfRange,
        PlacementOutOfRange,
        PlacementOccupied,
        CapacityReached,
        StorageFailed,
    }

    internal sealed record WidgetLayoutMutationResult(
        bool IsSuccess,
        WidgetLayoutSnapshot Snapshot,
        WidgetPlacement? Placement,
        WidgetLayoutMutationError Error,
        string? TechnicalError);

    internal sealed class WidgetLayoutCoordinator
    {
        private readonly Func<IReadOnlyList<WidgetCatalogItem>> _catalogProvider;
        private readonly WidgetLayoutStore _store;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private WidgetLayoutSnapshot _snapshot = WidgetLayoutSnapshot.Empty;

        internal WidgetLayoutCoordinator(
            Func<IReadOnlyList<WidgetCatalogItem>> catalogProvider,
            WidgetLayoutStore store)
        {
            _catalogProvider = catalogProvider
                ?? throw new ArgumentNullException(nameof(catalogProvider));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        internal WidgetLayoutSnapshot Snapshot => _snapshot;

        internal async Task<WidgetLayoutLoadResult> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var loaded = await _store.LoadAsync(cancellationToken);
                if (!loaded.IsSuccess)
                    return loaded;
                var reconciled = Reconcile(loaded.Snapshot, _catalogProvider());
                if (reconciled != loaded.Snapshot)
                {
                    var saved = await _store.SaveAsync(reconciled, cancellationToken);
                    if (!saved.IsSuccess)
                    {
                        return new WidgetLayoutLoadResult(
                            false,
                            _snapshot,
                            saved.Error);
                    }
                }
                _snapshot = reconciled;
                return new WidgetLayoutLoadResult(true, _snapshot, null);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task<WidgetLayoutMutationResult> AddAsync(
            string pluginId,
            string widgetId,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var item = FindCatalogItem(pluginId, widgetId);
                if (item is null)
                    return Failure(WidgetLayoutMutationError.CatalogItemNotFound);
                if (!item.MultipleInstances && _snapshot.Placements.Any(placement =>
                        Matches(placement, pluginId, widgetId)))
                {
                    return Failure(WidgetLayoutMutationError.MultipleInstancesNotAllowed);
                }
                if (_snapshot.Placements.Count >= WidgetLayoutStore.MaximumPlacements)
                    return Failure(WidgetLayoutMutationError.CapacityReached);

                var size = item.Definition.DefaultSize!;
                if (!TryFindOpenPosition(
                        _snapshot.Placements,
                        size.Columns,
                        size.Rows,
                        out var column,
                        out var row))
                {
                    return Failure(WidgetLayoutMutationError.CapacityReached);
                }

                var placement = new WidgetPlacement(
                    Guid.NewGuid().ToString("N"),
                    pluginId,
                    widgetId,
                    column,
                    row,
                    size.Columns,
                    size.Rows);
                return await CommitAsync(
                    _snapshot.Placements.Append(placement),
                    placement,
                    cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task<WidgetLayoutMutationResult> MoveResizeAsync(
            string instanceId,
            int column,
            int row,
            int columns,
            int rows,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var existing = _snapshot.Placements.FirstOrDefault(placement =>
                    string.Equals(
                        placement.InstanceId,
                        instanceId,
                        StringComparison.Ordinal));
                if (existing is null)
                    return Failure(WidgetLayoutMutationError.InstanceNotFound);
                var item = FindCatalogItem(existing.PluginId, existing.WidgetId);
                if (item is null)
                    return Failure(WidgetLayoutMutationError.CatalogItemNotFound);
                if (!IsSizeAllowed(item.Definition, columns, rows))
                    return Failure(WidgetLayoutMutationError.SizeOutOfRange);

                var updated = existing with
                {
                    Column = column,
                    Row = row,
                    Columns = columns,
                    Rows = rows,
                };
                if (!WidgetLayoutStore.IsPlacementValid(updated))
                    return Failure(WidgetLayoutMutationError.PlacementOutOfRange);
                if (_snapshot.Placements.Any(placement =>
                        !ReferenceEquals(placement, existing)
                        && WidgetLayoutStore.Overlaps(placement, updated)))
                {
                    return Failure(WidgetLayoutMutationError.PlacementOccupied);
                }
                if (updated == existing)
                {
                    return new WidgetLayoutMutationResult(
                        true,
                        _snapshot,
                        existing,
                        WidgetLayoutMutationError.None,
                        null);
                }

                return await CommitAsync(
                    _snapshot.Placements.Select(placement =>
                        ReferenceEquals(placement, existing) ? updated : placement),
                    updated,
                    cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task<WidgetLayoutMutationResult> RemoveAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var existing = _snapshot.Placements.FirstOrDefault(placement =>
                    string.Equals(
                        placement.InstanceId,
                        instanceId,
                        StringComparison.Ordinal));
                if (existing is null)
                    return Failure(WidgetLayoutMutationError.InstanceNotFound);
                return await CommitAsync(
                    _snapshot.Placements.Where(placement =>
                        !ReferenceEquals(placement, existing)),
                    existing,
                    cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<WidgetLayoutMutationResult> CommitAsync(
            IEnumerable<WidgetPlacement> placements,
            WidgetPlacement placement,
            CancellationToken cancellationToken)
        {
            var next = new WidgetLayoutSnapshot(
                checked(_snapshot.Revision + 1),
                placements.ToArray());
            var saved = await _store.SaveAsync(next, cancellationToken);
            if (!saved.IsSuccess)
            {
                return Failure(
                    WidgetLayoutMutationError.StorageFailed,
                    saved.Error);
            }
            _snapshot = next;
            return new WidgetLayoutMutationResult(
                true,
                _snapshot,
                placement,
                WidgetLayoutMutationError.None,
                null);
        }

        private WidgetCatalogItem? FindCatalogItem(string pluginId, string widgetId)
            => _catalogProvider().FirstOrDefault(item =>
                string.Equals(item.PluginId, pluginId, StringComparison.Ordinal)
                && string.Equals(item.WidgetId, widgetId, StringComparison.Ordinal));

        private WidgetLayoutMutationResult Failure(
            WidgetLayoutMutationError error,
            string? technicalError = null)
            => new(false, _snapshot, null, error, technicalError);

        private static WidgetLayoutSnapshot Reconcile(
            WidgetLayoutSnapshot snapshot,
            IReadOnlyList<WidgetCatalogItem> catalog)
        {
            var accepted = new List<WidgetPlacement>();
            var singletons = new HashSet<string>(StringComparer.Ordinal);
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            var changed = false;
            foreach (var placement in snapshot.Placements)
            {
                var item = catalog.FirstOrDefault(candidate =>
                    Matches(placement, candidate.PluginId, candidate.WidgetId));
                if (item is null || !WidgetLayoutStore.IsPlacementValid(placement))
                {
                    changed = true;
                    continue;
                }
                if (!instanceIds.Add(placement.InstanceId))
                {
                    changed = true;
                    continue;
                }
                var singletonKey = $"{placement.PluginId}\n{placement.WidgetId}";
                if (!item.MultipleInstances && !singletons.Add(singletonKey))
                {
                    changed = true;
                    continue;
                }
                if (!IsSizeAllowed(item.Definition, placement.Columns, placement.Rows)
                    || accepted.Any(existing => WidgetLayoutStore.Overlaps(
                        existing,
                        placement)))
                {
                    changed = true;
                    continue;
                }
                accepted.Add(placement);
            }

            return changed
                ? new WidgetLayoutSnapshot(
                    checked(snapshot.Revision + 1),
                    accepted)
                : snapshot;
        }

        private static bool IsSizeAllowed(
            PluginWidgetDefinition definition,
            int columns,
            int rows)
        {
            return columns >= (definition.MinSize?.Columns ?? 1)
                && rows >= (definition.MinSize?.Rows ?? 1)
                && columns <= (definition.MaxSize?.Columns ?? 24)
                && rows <= (definition.MaxSize?.Rows ?? 24);
        }

        private static bool TryFindOpenPosition(
            IReadOnlyList<WidgetPlacement> placements,
            int columns,
            int rows,
            out int column,
            out int row)
        {
            for (row = 0; row <= 100_000; row++)
            {
                for (column = 0; column <= 24 - columns; column++)
                {
                    var candidate = new WidgetPlacement(
                        "candidate",
                        "candidate",
                        "candidate",
                        column,
                        row,
                        columns,
                        rows);
                    if (!placements.Any(existing =>
                            WidgetLayoutStore.Overlaps(existing, candidate)))
                    {
                        return true;
                    }
                }
            }
            column = 0;
            row = 0;
            return false;
        }

        private static bool Matches(
            WidgetPlacement placement,
            string pluginId,
            string widgetId)
            => string.Equals(placement.PluginId, pluginId, StringComparison.Ordinal)
                && string.Equals(placement.WidgetId, widgetId, StringComparison.Ordinal);
    }
}
