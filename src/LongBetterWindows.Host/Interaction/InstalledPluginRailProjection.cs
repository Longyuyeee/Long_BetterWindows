using System.Globalization;
using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record InstalledPluginRailItem(
        string Id,
        string Name,
        string Version,
        PluginState State,
        bool IsRunning,
        bool HasUpdate,
        bool CanOpen,
        bool IsActive,
        string Monogram,
        string? IconPath,
        string StatusText,
        long RegistrationRevision);

    internal sealed record InstalledPluginRailReconcileResult(
        int Added,
        int Removed,
        int Moved,
        int Replaced);

    internal static class InstalledPluginRailProjection
    {
        private static readonly string[] IconFileNames =
        [
            "icon.png",
            "icon.ico",
            "plugin.png",
        ];

        public static IReadOnlyList<InstalledPluginRailItem> Build(
            IEnumerable<PluginEntry> plugins,
            IEnumerable<MarketplaceEntry>? catalog = null,
            string? query = null,
            string? activePluginId = null,
            Func<string, string>? localize = null)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            var marketplace = (catalog ?? [])
                .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            var normalizedQuery = query?.Trim();

            return plugins
                .Where(entry => Matches(entry, normalizedQuery))
                .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(entry => Project(
                    entry,
                    marketplace.GetValueOrDefault(entry.Id),
                    activePluginId,
                    localize))
                .ToArray();
        }

        public static InstalledPluginRailReconcileResult Reconcile(
            IList<InstalledPluginRailItem> current,
            IReadOnlyList<InstalledPluginRailItem> projected)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(projected);
            var added = 0;
            var removed = 0;
            var moved = 0;
            var replaced = 0;

            for (var index = current.Count - 1; index >= 0; index--)
            {
                if (!projected.Any(item => SameIdentity(item, current[index])))
                {
                    current.RemoveAt(index);
                    removed++;
                }
            }

            for (var targetIndex = 0; targetIndex < projected.Count; targetIndex++)
            {
                var target = projected[targetIndex];
                var currentIndex = IndexOf(current, target);
                if (currentIndex < 0)
                {
                    current.Insert(targetIndex, target);
                    added++;
                    continue;
                }

                if (currentIndex != targetIndex)
                {
                    var item = current[currentIndex];
                    current.RemoveAt(currentIndex);
                    current.Insert(targetIndex, item);
                    moved++;
                }

                if (current[targetIndex] != target)
                {
                    current[targetIndex] = target;
                    replaced++;
                }
            }

            return new InstalledPluginRailReconcileResult(
                added,
                removed,
                moved,
                replaced);
        }

        private static InstalledPluginRailItem Project(
            PluginEntry entry,
            MarketplaceEntry? marketplace,
            string? activePluginId,
            Func<string, string>? localize)
        {
            var isRunning = entry.State is PluginState.Running or PluginState.Background;
            var hasUpdate = marketplace is not null
                && LocalMarketplaceRepository.GetInstallState(
                    marketplace,
                    entry.Manifest.Version) == MarketplaceInstallState.UpdateAvailable;
            var stateKey = isRunning
                ? "toolcenter.running"
                : "toolcenter.stopped";
            var status = localize?.Invoke(stateKey)
                ?? (isRunning ? "Running" : "Stopped");
            if (hasUpdate)
            {
                var update = localize?.Invoke("plugins.updateAvailable")
                    ?? "Update available";
                status = $"{status} · {update}";
            }

            return new InstalledPluginRailItem(
                entry.Id,
                entry.DisplayName,
                entry.Manifest.Version,
                entry.State,
                isRunning,
                hasUpdate,
                entry.Instance is IHasMainUI
                    || entry.Manifest.Window is not null
                    || !string.IsNullOrWhiteSpace(entry.Manifest.EntryPoint),
                string.Equals(
                    entry.Id,
                    activePluginId,
                    StringComparison.OrdinalIgnoreCase),
                GetMonogram(entry.DisplayName, entry.Id),
                FindIconPath(entry.Directory),
                status,
                entry.RegistrationRevision);
        }

        private static bool Matches(PluginEntry entry, string? query)
            => string.IsNullOrWhiteSpace(query)
                || entry.DisplayName.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase)
                || entry.Id.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase);

        private static string GetMonogram(string name, string fallback)
        {
            var source = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            return string.IsNullOrEmpty(source)
                ? "?"
                : StringInfo.GetNextTextElement(source).ToUpper(CultureInfo.CurrentCulture);
        }

        internal static string? FindIconPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return null;
            foreach (var fileName in IconFileNames)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                        or NotSupportedException
                        or IOException
                        or UnauthorizedAccessException)
                {
                    return null;
                }
            }
            return null;
        }

        private static bool SameIdentity(
            InstalledPluginRailItem left,
            InstalledPluginRailItem right)
            => string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);

        private static int IndexOf(
            IList<InstalledPluginRailItem> items,
            InstalledPluginRailItem target)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (SameIdentity(items[index], target))
                    return index;
            }
            return -1;
        }
    }
}
