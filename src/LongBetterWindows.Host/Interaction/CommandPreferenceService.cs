using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    public sealed record CommandPreferenceSnapshot(
        bool IsEnabled,
        IReadOnlyList<string> Aliases)
    {
        public static CommandPreferenceSnapshot Default { get; } =
            new(true, Array.Empty<string>());
    }

    public sealed class CommandPreferenceService
    {
        private const string StorageKey = "command.preferences.v1";
        private const int MaxEntries = 512;
        public const int MaxAliases = 8;
        public const int MaxAliasLength = 32;

        private readonly IStorageService _storage;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _persistGate = new(1, 1);
        private Dictionary<string, PreferenceEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public CommandPreferenceService(IStorageService storage)
            => _storage = storage ?? throw new ArgumentNullException(nameof(storage));

        public async Task InitializeAsync()
        {
            var response = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Data))
                return;

            try
            {
                var persisted = JsonSerializer.Deserialize<List<PreferenceEntry>>(
                        response.Data)
                    ?? new List<PreferenceEntry>();
                var entries = new Dictionary<string, PreferenceEntry>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in persisted
                             .Where(item => !string.IsNullOrWhiteSpace(item.CommandKey))
                             .Take(MaxEntries))
                {
                    entries[entry.CommandKey] = entry with
                    {
                        Aliases = NormalizeAliases(entry.Aliases),
                    };
                }

                lock (_lock) _entries = entries;
            }
            catch (Exception exception)
            {
                Log.Debug(
                    exception,
                    "Local command preferences could not be loaded; defaults will be used");
            }
        }

        public CommandPreferenceSnapshot Get(string commandKey)
        {
            if (string.IsNullOrWhiteSpace(commandKey))
                return CommandPreferenceSnapshot.Default;
            lock (_lock)
            {
                return _entries.TryGetValue(commandKey, out var entry)
                    ? new CommandPreferenceSnapshot(
                        entry.IsEnabled,
                        entry.Aliases.ToArray())
                    : CommandPreferenceSnapshot.Default;
            }
        }

        public bool IsEnabled(string commandKey) => Get(commandKey).IsEnabled;

        public async Task SetEnabledAsync(string commandKey, bool isEnabled)
        {
            ValidateCommandKey(commandKey);
            lock (_lock)
            {
                var current = GetEntryUnsafe(commandKey);
                StoreOrRemoveDefaultUnsafe(current with { IsEnabled = isEnabled });
            }
            await PersistAsync().ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> SetAliasesAsync(
            string commandKey,
            IEnumerable<string>? aliases)
        {
            ValidateCommandKey(commandKey);
            var normalized = NormalizeAliases(aliases, rejectInvalid: true);
            lock (_lock)
            {
                var current = GetEntryUnsafe(commandKey);
                StoreOrRemoveDefaultUnsafe(current with { Aliases = normalized });
            }
            await PersistAsync().ConfigureAwait(false);
            return normalized;
        }

        public static IReadOnlyList<string> ParseAliases(string? value)
            => NormalizeAliases((value ?? string.Empty).Split(
                [',', ';', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries),
                rejectInvalid: true);

        private PreferenceEntry GetEntryUnsafe(string commandKey)
            => _entries.TryGetValue(commandKey, out var current)
                ? current
                : new PreferenceEntry { CommandKey = commandKey };

        private void StoreOrRemoveDefaultUnsafe(PreferenceEntry entry)
        {
            if (entry.IsEnabled && entry.Aliases.Count == 0)
                _entries.Remove(entry.CommandKey);
            else
                _entries[entry.CommandKey] = entry;
        }

        private async Task PersistAsync()
        {
            await _persistGate.WaitAsync().ConfigureAwait(false);
            try
            {
                List<PreferenceEntry> snapshot;
                lock (_lock) snapshot = _entries.Values.Take(MaxEntries).ToList();
                if (snapshot.Count == 0)
                {
                    await _storage.DeleteAsync(StorageKey).ConfigureAwait(false);
                    return;
                }

                var response = await _storage.SetAsync(
                    StorageKey,
                    JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);
                if (!response.IsSuccess)
                    Log.Debug("Local command preferences could not be saved");
            }
            finally
            {
                _persistGate.Release();
            }
        }

        private static IReadOnlyList<string> NormalizeAliases(
            IEnumerable<string>? aliases,
            bool rejectInvalid = false)
        {
            var normalized = new List<string>();
            foreach (var raw in aliases ?? Array.Empty<string>())
            {
                var alias = raw?.Trim() ?? string.Empty;
                if (alias.Length == 0)
                    continue;
                if (alias.Length > MaxAliasLength || alias.Any(char.IsControl))
                {
                    if (rejectInvalid)
                        throw new ArgumentException(
                            $"Each alias must be at most {MaxAliasLength} characters and contain no control characters.");
                    continue;
                }
                if (normalized.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (normalized.Count >= MaxAliases)
                {
                    if (rejectInvalid)
                        throw new ArgumentException($"A command can have at most {MaxAliases} aliases.");
                    break;
                }
                normalized.Add(alias);
            }
            return normalized.ToArray();
        }

        private static void ValidateCommandKey(string commandKey)
        {
            if (string.IsNullOrWhiteSpace(commandKey))
                throw new ArgumentException("A command key is required.", nameof(commandKey));
        }

        private sealed record PreferenceEntry
        {
            public string CommandKey { get; init; } = string.Empty;
            public bool IsEnabled { get; init; } = true;
            public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
        }
    }
}
