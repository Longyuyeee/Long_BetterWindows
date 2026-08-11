using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record SearchPreferenceScore(bool IsPinned, int Score);

    public sealed class SearchPreferenceService
    {
        private const string StorageKey = "search.preferences.v1";
        private const int MaxEntries = 256;
        private readonly IStorageService _storage;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _persistGate = new(1, 1);
        private Dictionary<string, PreferenceEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public SearchPreferenceService(IStorageService storage)
        {
            _storage = storage;
        }

        public async Task InitializeAsync()
        {
            var response = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Data))
                return;

            try
            {
                var entries = JsonSerializer.Deserialize<List<PreferenceEntry>>(response.Data)
                    ?? new List<PreferenceEntry>();
                lock (_lock)
                {
                    _entries = entries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.ResultId))
                        .GroupBy(entry => entry.ResultId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.OrderByDescending(item => item.LastUsedAt).First(),
                            StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "本地搜索偏好加载失败，将使用空偏好");
            }
        }

        public SearchPreferenceScore GetScore(
            string resultId,
            DateTimeOffset now,
            bool hasActiveQuery = false)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(resultId, out var entry))
                    return new SearchPreferenceScore(false, 0);

                var frequency = Math.Min(
                    180,
                    (int)Math.Round(Math.Log2(entry.UseCount + 1) * 35));
                var age = now - entry.LastUsedAt;
                var recency = entry.UseCount == 0 ? 0
                    : age <= TimeSpan.FromHours(1) ? 220
                    : age <= TimeSpan.FromDays(1) ? 160
                    : age <= TimeSpan.FromDays(7) ? 90
                    : age <= TimeSpan.FromDays(30) ? 40
                    : 0;
                var score = hasActiveQuery
                    ? (entry.IsPinned ? 80 : 0)
                        + Math.Min(40, (frequency + recency) / 8)
                    : (entry.IsPinned ? 1500 : 0) + frequency + recency;
                return new SearchPreferenceScore(entry.IsPinned, score);
            }
        }

        public IReadOnlyList<string> GetPinnedResultIds()
        {
            lock (_lock)
                return _entries.Values
                    .Where(entry => entry.IsPinned)
                    .OrderBy(entry => entry.PinnedOrder ?? int.MaxValue)
                    .ThenByDescending(entry => entry.LastUsedAt)
                    .Select(entry => entry.ResultId)
                    .ToList();
        }

        public IReadOnlyList<string> GetRecentResultIds(int maxResults = 12)
        {
            lock (_lock)
                return _entries.Values
                    .Where(entry => entry.UseCount > 0)
                    .OrderByDescending(entry => entry.LastUsedAt)
                    .Take(Math.Max(0, maxResults))
                    .Select(entry => entry.ResultId)
                    .ToList();
        }

        public async Task<bool> TogglePinnedAsync(string resultId)
        {
            PreferenceEntry updated;
            lock (_lock)
            {
                if (!_entries.TryGetValue(resultId, out var current))
                    current = new PreferenceEntry { ResultId = resultId };
                var willBePinned = !current.IsPinned;
                var nextOrder = _entries.Values
                    .Where(entry => entry.IsPinned && entry.PinnedOrder.HasValue)
                    .Select(entry => entry.PinnedOrder!.Value)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;
                updated = current with
                {
                    IsPinned = willBePinned,
                    PinnedOrder = willBePinned ? nextOrder : null,
                    LastUsedAt = current.LastUsedAt == DateTimeOffset.MinValue
                        ? DateTimeOffset.UtcNow
                        : current.LastUsedAt,
                };
                _entries[resultId] = updated;
                TrimUnsafe();
            }

            await PersistAsync();
            return updated.IsPinned;
        }

        public async Task<bool> MovePinnedAsync(string resultId, int targetIndex)
        {
            lock (_lock)
            {
                var pinned = _entries.Values
                    .Where(entry => entry.IsPinned)
                    .OrderBy(entry => entry.PinnedOrder ?? int.MaxValue)
                    .ThenByDescending(entry => entry.LastUsedAt)
                    .ToList();
                var currentIndex = pinned.FindIndex(entry => string.Equals(
                    entry.ResultId, resultId, StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0 || pinned.Count < 2)
                    return false;

                targetIndex = Math.Clamp(targetIndex, 0, pinned.Count - 1);
                if (currentIndex == targetIndex)
                    return false;

                var moving = pinned[currentIndex];
                pinned.RemoveAt(currentIndex);
                pinned.Insert(targetIndex, moving);
                for (var index = 0; index < pinned.Count; index++)
                {
                    var entry = pinned[index];
                    _entries[entry.ResultId] = entry with { PinnedOrder = index };
                }
            }

            await PersistAsync();
            return true;
        }

        public async Task RecordUseAsync(string resultId)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(resultId, out var current))
                    current = new PreferenceEntry { ResultId = resultId };
                _entries[resultId] = current with
                {
                    UseCount = current.UseCount + 1,
                    LastUsedAt = DateTimeOffset.UtcNow,
                };
                TrimUnsafe();
            }

            await PersistAsync();
        }

        public async Task ClearAsync()
        {
            lock (_lock) _entries.Clear();
            await _persistGate.WaitAsync();
            try
            {
                await _storage.DeleteAsync(StorageKey);
            }
            finally
            {
                _persistGate.Release();
            }
        }

        private void TrimUnsafe()
        {
            if (_entries.Count <= MaxEntries) return;
            foreach (var id in _entries.Values
                         .Where(entry => !entry.IsPinned)
                         .OrderBy(entry => entry.LastUsedAt)
                         .Take(_entries.Count - MaxEntries)
                         .Select(entry => entry.ResultId)
                         .ToList())
            {
                _entries.Remove(id);
            }
        }

        private async Task PersistAsync()
        {
            await _persistGate.WaitAsync();
            try
            {
                List<PreferenceEntry> snapshot;
                lock (_lock) snapshot = _entries.Values.ToList();
                var json = JsonSerializer.Serialize(snapshot);
                var response = await _storage.SetAsync(StorageKey, json);
                if (!response.IsSuccess)
                    Log.Debug("本地搜索偏好保存失败");
            }
            finally
            {
                _persistGate.Release();
            }
        }

        private sealed record PreferenceEntry
        {
            public string ResultId { get; init; } = string.Empty;
            public bool IsPinned { get; init; }
            public int? PinnedOrder { get; init; }
            public int UseCount { get; init; }
            public DateTimeOffset LastUsedAt { get; init; } = DateTimeOffset.MinValue;
        }
    }
}
