using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record SuperPanelCustomGroup(
        string Id,
        string Title,
        IReadOnlyList<string> ResultIds);

    public sealed class SuperPanelGroupService
    {
        private const string StorageKey = "super-panel.groups.v1";
        private const int MaxGroups = 8;
        private const int MaxResultsPerGroup = 64;
        private const int MaxTitleLength = 24;
        private readonly IStorageService _storage;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _persistGate = new(1, 1);
        private List<GroupEntry> _groups = new();

        public SuperPanelGroupService(IStorageService storage)
        {
            _storage = storage;
        }

        public async Task InitializeAsync()
        {
            var response = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Data)) return;

            try
            {
                var groups = JsonSerializer.Deserialize<List<GroupEntry>>(response.Data)
                    ?? new List<GroupEntry>();
                lock (_lock)
                {
                    _groups = groups
                        .Where(group => IsCustomGroupId(group.Id))
                        .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(group => Normalize(group.First()))
                        .Where(group => group.Title.Length > 0)
                        .Take(MaxGroups)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "超级面板自定义分组加载失败，将使用空分组");
            }
        }

        public IReadOnlyList<SuperPanelCustomGroup> GetGroups()
        {
            lock (_lock)
                return _groups.Select(ToPublicGroup).ToList();
        }

        public async Task<SuperPanelCustomGroup?> CreateAsync(string title)
        {
            GroupEntry created;
            lock (_lock)
            {
                if (_groups.Count >= MaxGroups) return null;
                var normalizedTitle = NormalizeTitle(title);
                if (normalizedTitle.Length == 0) return null;
                created = new GroupEntry
                {
                    Id = "folder:" + Guid.NewGuid().ToString("N"),
                    Title = MakeUniqueTitle(normalizedTitle, null),
                };
                _groups.Add(created);
            }
            await PersistAsync();
            return ToPublicGroup(created);
        }

        public async Task<bool> RenameAsync(string groupId, string title)
        {
            lock (_lock)
            {
                var index = FindIndex(groupId);
                var normalizedTitle = NormalizeTitle(title);
                if (index < 0 || normalizedTitle.Length == 0) return false;
                _groups[index] = _groups[index] with
                {
                    Title = MakeUniqueTitle(normalizedTitle, groupId),
                };
            }
            await PersistAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(string groupId)
        {
            lock (_lock)
            {
                var index = FindIndex(groupId);
                if (index < 0) return false;
                _groups.RemoveAt(index);
            }
            await PersistAsync();
            return true;
        }

        public async Task<bool> AddResultAsync(string groupId, string resultId)
        {
            lock (_lock)
            {
                var index = FindIndex(groupId);
                if (index < 0 || string.IsNullOrWhiteSpace(resultId)) return false;
                var ids = _groups[index].ResultIds
                    .Where(id => !string.Equals(id, resultId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ids.Add(resultId);
                _groups[index] = _groups[index] with
                {
                    ResultIds = ids.TakeLast(MaxResultsPerGroup).ToList(),
                };
            }
            await PersistAsync();
            return true;
        }

        public async Task<bool> MoveResultAsync(string groupId, string resultId, int targetIndex)
        {
            lock (_lock)
            {
                var index = FindIndex(groupId);
                if (index < 0) return false;
                var ids = _groups[index].ResultIds.ToList();
                var currentIndex = ids.FindIndex(id => string.Equals(
                    id, resultId, StringComparison.OrdinalIgnoreCase));
                if (currentIndex < 0 || ids.Count < 2) return false;
                targetIndex = Math.Clamp(targetIndex, 0, ids.Count - 1);
                if (currentIndex == targetIndex) return false;
                var moving = ids[currentIndex];
                ids.RemoveAt(currentIndex);
                ids.Insert(targetIndex, moving);
                _groups[index] = _groups[index] with { ResultIds = ids };
            }
            await PersistAsync();
            return true;
        }

        public async Task<bool> RemoveResultAsync(string groupId, string resultId)
        {
            lock (_lock)
            {
                var index = FindIndex(groupId);
                if (index < 0) return false;
                var ids = _groups[index].ResultIds
                    .Where(id => !string.Equals(id, resultId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (ids.Count == _groups[index].ResultIds.Count) return false;
                _groups[index] = _groups[index] with { ResultIds = ids };
            }
            await PersistAsync();
            return true;
        }

        public async Task ClearAsync()
        {
            lock (_lock) _groups.Clear();
            await _persistGate.WaitAsync();
            try { await _storage.DeleteAsync(StorageKey); }
            finally { _persistGate.Release(); }
        }

        public static bool IsCustomGroupId(string? groupId)
            => groupId?.StartsWith("folder:", StringComparison.OrdinalIgnoreCase) == true;

        private int FindIndex(string groupId)
            => _groups.FindIndex(group => string.Equals(
                group.Id, groupId, StringComparison.OrdinalIgnoreCase));

        private string MakeUniqueTitle(string title, string? exceptGroupId)
        {
            var candidate = title;
            for (var suffix = 2; _groups.Any(group =>
                     !string.Equals(group.Id, exceptGroupId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(group.Title, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
            {
                var marker = $" {suffix}";
                candidate = title[..Math.Min(title.Length, MaxTitleLength - marker.Length)] + marker;
            }
            return candidate;
        }

        private static GroupEntry Normalize(GroupEntry group) => group with
        {
            Title = NormalizeTitle(group.Title),
            ResultIds = (group.ResultIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxResultsPerGroup)
                .ToList(),
        };

        private static string NormalizeTitle(string? title)
        {
            var normalized = string.Join(" ", (title ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return normalized[..Math.Min(normalized.Length, MaxTitleLength)];
        }

        private static SuperPanelCustomGroup ToPublicGroup(GroupEntry group)
            => new(group.Id, group.Title, group.ResultIds.ToList());

        private async Task PersistAsync()
        {
            await _persistGate.WaitAsync();
            try
            {
                List<GroupEntry> snapshot;
                lock (_lock) snapshot = _groups.ToList();
                var response = await _storage.SetAsync(
                    StorageKey, JsonSerializer.Serialize(snapshot));
                if (!response.IsSuccess)
                    Log.Debug("超级面板自定义分组保存失败");
            }
            finally { _persistGate.Release(); }
        }

        private sealed record GroupEntry
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public List<string> ResultIds { get; init; } = new();
        }
    }
}
