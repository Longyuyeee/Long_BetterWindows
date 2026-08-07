using System.Text.Json;
using System.Windows.Threading;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record CommandHotkeyState(
        string CommandKey,
        string Hotkey,
        bool IsRegistered,
        bool IsPaused,
        string? ConflictOwner);

    internal sealed record CommandHotkeyUpdateResult(
        bool IsSuccess,
        ApiErrorCode ErrorCode = ApiErrorCode.None,
        string? ErrorMessage = null,
        string? ConflictOwner = null)
    {
        public static CommandHotkeyUpdateResult Success() => new(true);

        public static CommandHotkeyUpdateResult Failure(
            ApiErrorCode errorCode,
            string? errorMessage = null,
            string? conflictOwner = null)
            => new(false, errorCode, errorMessage, conflictOwner);
    }

    internal sealed class CommandHotkeyCoordinator : IDisposable
    {
        private const string StorageKey = "command.hotkeys.v1";
        private const string OwnerPrefix = "command:";
        private readonly IStorageService _storage;
        private readonly IHotKeyService _hotkeys;
        private readonly PluginRegistry _plugins;
        private readonly CommandExecutor _executor;
        private readonly Dispatcher _dispatcher;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _transactionGate = new(1, 1);
        private Dictionary<string, string> _configured =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _active;
        private bool _subscribed;
        private int _disposed;

        public CommandHotkeyCoordinator(
            IStorageService storage,
            IHotKeyService hotkeys,
            PluginRegistry plugins,
            CommandExecutor executor,
            Dispatcher dispatcher)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task InitializeAsync()
        {
            var response = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Data))
                return;

            try
            {
                var entries = JsonSerializer.Deserialize<List<PersistedEntry>>(
                        response.Data)
                    ?? new List<PersistedEntry>();
                lock (_lock)
                {
                    _configured = entries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.CommandKey)
                            && !string.IsNullOrWhiteSpace(entry.Hotkey))
                        .GroupBy(entry => entry.CommandKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Last().Hotkey.Trim(),
                            StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception exception)
            {
                Log.Debug(
                    exception,
                    "Command hotkey preferences could not be loaded; defaults will be used");
            }
        }

        public async Task ActivateAsync()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (!_subscribed)
            {
                _plugins.PluginsChanged += Plugins_PluginsChanged;
                _subscribed = true;
            }
            _active = true;
            await ReconcileAllAsync().ConfigureAwait(false);
        }

        public CommandHotkeyState GetState(string commandKey)
        {
            string hotkey;
            lock (_lock)
                hotkey = _configured.TryGetValue(commandKey, out var configured)
                    ? configured
                    : string.Empty;
            if (hotkey.Length == 0)
                return new CommandHotkeyState(
                    commandKey,
                    string.Empty,
                    false,
                    !_plugins.Commands.IsEnabled(commandKey),
                    null);

            var owner = _hotkeys.GetOwner(hotkey);
            var expectedOwner = BuildOwner(commandKey);
            return new CommandHotkeyState(
                commandKey,
                hotkey,
                string.Equals(owner, expectedOwner, StringComparison.OrdinalIgnoreCase),
                !_plugins.Commands.IsEnabled(commandKey),
                owner is not null
                    && !string.Equals(owner, expectedOwner, StringComparison.OrdinalIgnoreCase)
                        ? owner
                        : null);
        }

        public async Task<CommandHotkeyUpdateResult> ChangeAsync(
            string commandKey,
            string? requestedHotkey)
        {
            if (_plugins.Commands.Get(commandKey) is null)
            {
                return CommandHotkeyUpdateResult.Failure(
                    ApiErrorCode.NotFound,
                    "The command is not registered.");
            }

            var newHotkey = requestedHotkey?.Trim() ?? string.Empty;
            if (newHotkey.Length > 0 && !_plugins.Commands.IsEnabled(commandKey))
            {
                return CommandHotkeyUpdateResult.Failure(
                    ApiErrorCode.InvalidArgument,
                    "Enable the command before assigning a shortcut.");
            }

            await _transactionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var oldHotkey = GetConfigured(commandKey);
                return newHotkey.Length == 0
                    ? await RemoveCoreAsync(commandKey, oldHotkey).ConfigureAwait(false)
                    : await ChangeCoreAsync(
                        commandKey,
                        oldHotkey,
                        newHotkey).ConfigureAwait(false);
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        public async Task RefreshCommandAsync(string commandKey)
        {
            if (!_active || Volatile.Read(ref _disposed) != 0)
                return;
            await _transactionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ReconcileCommandCoreAsync(commandKey).ConfigureAwait(false);
            }
            finally
            {
                _transactionGate.Release();
            }
        }

        private async Task<CommandHotkeyUpdateResult> ChangeCoreAsync(
            string commandKey,
            string oldHotkey,
            string newHotkey)
        {
            var ownerId = BuildOwner(commandKey);
            var existingOwner = _hotkeys.GetOwner(newHotkey);
            if (existingOwner is not null
                && !string.Equals(existingOwner, ownerId, StringComparison.OrdinalIgnoreCase))
            {
                return CommandHotkeyUpdateResult.Failure(
                    ApiErrorCode.HotKeyConflict,
                    "The shortcut is already in use.",
                    existingOwner);
            }

            var oldWasRegistered = oldHotkey.Length > 0
                && string.Equals(
                    _hotkeys.GetOwner(oldHotkey),
                    ownerId,
                    StringComparison.OrdinalIgnoreCase);
            HostApiResponse runtimeResult;
            if (string.Equals(
                    Canonicalize(oldHotkey),
                    Canonicalize(newHotkey),
                    StringComparison.OrdinalIgnoreCase)
                && oldWasRegistered)
            {
                runtimeResult = HostApiResponse.Success();
            }
            else if (oldWasRegistered)
            {
                runtimeResult = await _hotkeys.ChangeHotkeyAsync(
                    oldHotkey,
                    newHotkey,
                    ownerId,
                    () => QueueExecution(commandKey)).ConfigureAwait(false);
            }
            else if (existingOwner is null)
            {
                runtimeResult = await _hotkeys.RegisterAsync(
                    newHotkey,
                    ownerId,
                    () => QueueExecution(commandKey)).ConfigureAwait(false);
            }
            else
            {
                runtimeResult = HostApiResponse.Success();
            }

            if (!runtimeResult.IsSuccess)
            {
                return CommandHotkeyUpdateResult.Failure(
                    runtimeResult.ErrorCode,
                    runtimeResult.ErrorMessage,
                    runtimeResult.ErrorCode == ApiErrorCode.HotKeyConflict
                        ? _hotkeys.GetOwner(newHotkey)
                        : null);
            }

            var candidate = SnapshotConfigured();
            candidate[commandKey] = newHotkey;
            var persistence = await PersistAsync(candidate).ConfigureAwait(false);
            if (!persistence.IsSuccess)
            {
                var rollback = oldWasRegistered
                    ? await _hotkeys.ChangeHotkeyAsync(
                        newHotkey,
                        oldHotkey,
                        ownerId,
                        () => QueueExecution(commandKey)).ConfigureAwait(false)
                    : await _hotkeys.UnregisterAsync(newHotkey).ConfigureAwait(false);
                var detail = persistence.ErrorMessage;
                if (!rollback.IsSuccess)
                    detail += $" Rollback failed: {rollback.ErrorMessage}";
                return CommandHotkeyUpdateResult.Failure(
                    persistence.ErrorCode,
                    detail);
            }

            lock (_lock) _configured = candidate;
            return CommandHotkeyUpdateResult.Success();
        }

        private async Task<CommandHotkeyUpdateResult> RemoveCoreAsync(
            string commandKey,
            string oldHotkey)
        {
            if (oldHotkey.Length == 0)
                return CommandHotkeyUpdateResult.Success();
            var ownerId = BuildOwner(commandKey);
            var oldWasRegistered = string.Equals(
                _hotkeys.GetOwner(oldHotkey),
                ownerId,
                StringComparison.OrdinalIgnoreCase);
            if (oldWasRegistered)
            {
                var unregister = await _hotkeys.UnregisterAsync(oldHotkey)
                    .ConfigureAwait(false);
                if (!unregister.IsSuccess)
                {
                    return CommandHotkeyUpdateResult.Failure(
                        unregister.ErrorCode,
                        unregister.ErrorMessage);
                }
            }

            var candidate = SnapshotConfigured();
            candidate.Remove(commandKey);
            var persistence = await PersistAsync(candidate).ConfigureAwait(false);
            if (!persistence.IsSuccess)
            {
                HostApiResponse? rollback = null;
                if (oldWasRegistered)
                {
                    rollback = await _hotkeys.RegisterAsync(
                        oldHotkey,
                        ownerId,
                        () => QueueExecution(commandKey)).ConfigureAwait(false);
                }
                var detail = persistence.ErrorMessage;
                if (rollback is { IsSuccess: false })
                    detail += $" Rollback failed: {rollback.ErrorMessage}";
                return CommandHotkeyUpdateResult.Failure(
                    persistence.ErrorCode,
                    detail);
            }

            lock (_lock) _configured = candidate;
            return CommandHotkeyUpdateResult.Success();
        }

        private async Task ReconcileAllAsync()
        {
            string[] commandKeys;
            lock (_lock) commandKeys = _configured.Keys.ToArray();
            foreach (var commandKey in commandKeys)
                await RefreshCommandAsync(commandKey).ConfigureAwait(false);
        }

        private async Task ReconcileCommandCoreAsync(string commandKey)
        {
            var hotkey = GetConfigured(commandKey);
            if (hotkey.Length == 0)
                return;
            var ownerId = BuildOwner(commandKey);
            var owner = _hotkeys.GetOwner(hotkey);
            var commandAvailable = _plugins.Commands.Get(commandKey) is not null
                && _plugins.Commands.IsEnabled(commandKey);
            if (!commandAvailable)
            {
                if (string.Equals(owner, ownerId, StringComparison.OrdinalIgnoreCase))
                    await _hotkeys.UnregisterAsync(hotkey).ConfigureAwait(false);
                return;
            }

            if (owner is null)
            {
                var result = await _hotkeys.RegisterAsync(
                    hotkey,
                    ownerId,
                    () => QueueExecution(commandKey)).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    Log.Debug(
                        "Command hotkey {Hotkey} could not be restored for {CommandKey}: {Error}",
                        hotkey,
                        commandKey,
                        result.ErrorMessage);
                }
            }
        }

        private async Task<HostApiResponse> PersistAsync(
            Dictionary<string, string> candidate)
        {
            if (candidate.Count == 0)
                return await _storage.DeleteAsync(StorageKey).ConfigureAwait(false);
            var entries = candidate
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new PersistedEntry(pair.Key, pair.Value))
                .ToArray();
            return await _storage.SetAsync(
                StorageKey,
                JsonSerializer.Serialize(entries)).ConfigureAwait(false);
        }

        private Dictionary<string, string> SnapshotConfigured()
        {
            lock (_lock)
                return new Dictionary<string, string>(
                    _configured,
                    StringComparer.OrdinalIgnoreCase);
        }

        private string GetConfigured(string commandKey)
        {
            lock (_lock)
                return _configured.TryGetValue(commandKey, out var hotkey)
                    ? hotkey
                    : string.Empty;
        }

        private void Plugins_PluginsChanged() => QueueReconcile();

        private void QueueReconcile()
        {
            if (!_active || Volatile.Read(ref _disposed) != 0)
                return;
            _ = _dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await ReconcileAllAsync();
                }
                catch (Exception exception)
                {
                    Log.Debug(exception, "Command hotkeys could not be reconciled");
                }
            }));
        }

        private void QueueExecution(string commandKey)
        {
            _ = _dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var result = await _executor.ExecuteAsync(commandKey);
                    if (!result.IsSuccess)
                    {
                        Log.Warning(
                            "Command hotkey execution failed for {CommandKey}: {Error}",
                            commandKey,
                            result.Message);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(
                        exception,
                        "Command hotkey execution failed for {CommandKey}",
                        commandKey);
                }
            }));
        }

        internal static string BuildOwner(string commandKey)
            => OwnerPrefix + commandKey;

        private static string Canonicalize(string hotkey)
            => string.Join(
                "+",
                hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim().ToUpperInvariant()));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (_subscribed)
            {
                _plugins.PluginsChanged -= Plugins_PluginsChanged;
                _subscribed = false;
            }
            _active = false;
        }

        private sealed record PersistedEntry(string CommandKey, string Hotkey);
    }
}
