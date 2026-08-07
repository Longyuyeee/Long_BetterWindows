using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record PluginSettingsModuleState(
        string Id,
        string Name,
        string Version,
        string RuntimeLabel,
        PluginState State,
        bool IsRunning,
        string StatusText,
        string ToggleText,
        string Hotkey,
        bool HasHotkey,
        bool HasSettings,
        bool CanOpen,
        int CapabilityCount,
        int TotalCalls,
        DateTime? LastCallTime,
        long RegistrationRevision);

    internal sealed record PluginCommandModuleItemState(
        string Key,
        string ResultId,
        string Title,
        string Description,
        string AliasSummary,
        string InputSummary,
        bool IsPinned,
        string PinText,
        bool IsEnabled,
        string CustomAliasesInitial,
        string HotkeyStatus,
        bool CanEditHotkey,
        bool CanClearHotkey,
        string HotkeyInitial,
        string EnabledAutomationName,
        string PinAutomationName,
        string AliasesAutomationName,
        string AliasesSaveAutomationName,
        string HotkeyAutomationName,
        string HotkeySaveAutomationName,
        string HotkeyClearAutomationName,
        string HotkeyStatusAutomationName)
    {
        public string CustomAliasesText { get; set; } = CustomAliasesInitial;
        public string HotkeyText { get; set; } = HotkeyInitial;
    }

    internal static class PluginSettingsModuleProjection
    {
        public static PluginSettingsModuleState Build(
            PluginEntry entry,
            PluginUsageStats? usage = null,
            Func<string, string>? localize = null)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var isRunning = entry.State is PluginState.Running or PluginState.Background;
            var statusKey = isRunning
                ? "plugins.status.runningVersion"
                : "plugins.status.stoppedVersion";
            var statusFormat = Text(
                localize,
                statusKey,
                isRunning ? "Running · v{0}" : "Stopped · v{0}");

            return new PluginSettingsModuleState(
                entry.Id,
                entry.DisplayName,
                entry.Manifest.Version,
                entry.Manifest.Runtime switch
                {
                    "webview" => "Web",
                    "csharp-script" => "Script",
                    _ => "DLL",
                },
                entry.State,
                isRunning,
                string.Format(statusFormat, entry.Manifest.Version),
                Text(
                    localize,
                    isRunning ? "action.disable" : "action.enable",
                    isRunning ? "Disable" : "Enable"),
                PluginRegistry.GetPluginHotkey(entry) ?? string.Empty,
                !string.IsNullOrWhiteSpace(PluginRegistry.GetPluginHotkey(entry)),
                entry.Instance is IHasSettingsUI,
                entry.Instance is IHasMainUI
                    || entry.Manifest.Window is not null
                    || !string.IsNullOrWhiteSpace(entry.Manifest.EntryPoint),
                entry.Manifest.Capabilities.Count,
                usage?.TotalCalls ?? 0,
                usage?.LastCallTime,
                entry.RegistrationRevision);
        }

        public static IReadOnlyList<PluginCommandModuleItemState> BuildCommands(
            PluginEntry entry,
            IEnumerable<CommandDescriptor> commands,
            IReadOnlyCollection<string>? pinnedResultIds = null,
            Func<string, string>? localize = null,
            CommandPreferenceService? commandPreferences = null,
            CommandHotkeyCoordinator? commandHotkeys = null)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(commands);
            var pinned = new HashSet<string>(
                pinnedResultIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return commands
                .Where(command => string.Equals(
                    command.PluginId,
                    entry.Id,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
                .Select(command =>
                {
                    var resultId = CommandSearchResultIdentity.BuildResultId(
                        command.Key);
                    var aliases = command.Command.Aliases
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var inputs = command.Command.AcceptedInputs
                        .Distinct()
                        .Select(input => Text(
                            localize,
                            $"plugins.command.input.{input.ToString().ToLowerInvariant()}",
                            input.ToString()))
                        .ToArray();
                    var isPinned = pinned.Contains(resultId);
                    var preference = commandPreferences?.Get(command.Key)
                        ?? CommandPreferenceSnapshot.Default;
                    var hotkey = commandHotkeys?.GetState(command.Key)
                        ?? new CommandHotkeyState(
                            command.Key,
                            string.Empty,
                            false,
                            !preference.IsEnabled,
                            null);
                    var hotkeyStatus = hotkey.Hotkey.Length == 0
                        ? Text(
                            localize,
                            "plugins.command.hotkey.none",
                            "No command shortcut")
                        : hotkey.IsPaused
                            ? Text(
                                localize,
                                "plugins.command.hotkey.paused",
                                "Paused while command is disabled")
                            : hotkey.IsRegistered
                                ? Text(
                                    localize,
                                    "plugins.command.hotkey.registered",
                                    "Shortcut registered")
                                : hotkey.ConflictOwner is not null
                                    ? string.Format(
                                        Text(
                                            localize,
                                            "plugins.command.hotkey.conflict",
                                            "Unavailable: used by {0}"),
                                        hotkey.ConflictOwner)
                                    : Text(
                                        localize,
                                        "plugins.command.hotkey.pending",
                                        "Waiting for registration");
                    var pinText = Text(
                        localize,
                        isPinned ? "action.unpin" : "action.pin",
                        isPinned ? "Unpin" : "Pin");
                    return new PluginCommandModuleItemState(
                        command.Key,
                        resultId,
                        command.Title,
                        command.Description,
                        aliases.Length == 0
                            ? Text(
                                localize,
                                "plugins.command.aliases.none",
                                "No declared keywords")
                            : string.Format(
                                Text(
                                    localize,
                                    "plugins.command.aliases",
                                    "Keywords: {0}"),
                                string.Join(" · ", aliases)),
                        string.Format(
                            Text(
                                localize,
                                "plugins.command.inputs",
                                "Inputs: {0}"),
                            string.Join(" · ", inputs)),
                        isPinned,
                        pinText,
                        preference.IsEnabled,
                        string.Join(", ", preference.Aliases),
                        hotkeyStatus,
                        preference.IsEnabled,
                        hotkey.Hotkey.Length > 0,
                        hotkey.Hotkey,
                        FormatText(
                            localize,
                            "plugins.command.enabledA11y",
                            "{0}: enabled in search and workflows",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.pinA11y",
                            "{0}: {1}",
                            command.Title,
                            pinText),
                        FormatText(
                            localize,
                            "plugins.command.aliasesA11y",
                            "{0}: custom aliases",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.aliasesSaveA11y",
                            "{0}: save custom aliases",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.hotkeyA11y",
                            "{0}: command shortcut",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.hotkeySaveA11y",
                            "{0}: apply command shortcut",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.hotkeyClearA11y",
                            "{0}: clear command shortcut",
                            command.Title),
                        FormatText(
                            localize,
                            "plugins.command.hotkeyStatusA11y",
                            "{0}: {1}",
                            command.Title,
                            hotkeyStatus));
                })
                .ToArray();
        }

        private static string FormatText(
            Func<string, string>? localize,
            string key,
            string fallback,
            params object[] arguments)
            => string.Format(Text(localize, key, fallback), arguments);

        private static string Text(
            Func<string, string>? localize,
            string key,
            string fallback)
        {
            var value = localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
