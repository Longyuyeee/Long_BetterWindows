using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed record WidgetCatalogItem(
        string PluginId,
        string PluginName,
        string PluginVersion,
        string PluginDirectory,
        PluginWidgetDefinition Definition,
        string? IconPath)
    {
        internal string WidgetId => Definition.Id;
        internal string Title => Definition.Title;
        internal string Description => Definition.Description;
        internal bool MultipleInstances => Definition.MultipleInstances;
    }

    internal static class WidgetCatalogProjection
    {
        internal static IReadOnlyList<WidgetCatalogItem> Build(
            IEnumerable<PluginEntry> plugins)
        {
            ArgumentNullException.ThrowIfNull(plugins);
            return plugins
                .Where(entry => string.Equals(
                    entry.Manifest.Runtime,
                    "webview",
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Manifest.Widgets.Select(widget =>
                    new WidgetCatalogItem(
                        entry.Id,
                        entry.DisplayName,
                        entry.Manifest.Version,
                        entry.Directory,
                        widget,
                        ResolveIcon(entry.Directory, widget.Icon))))
                .OrderBy(item => item.PluginName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.PluginId, StringComparer.Ordinal)
                .ThenBy(item => item.WidgetId, StringComparer.Ordinal)
                .ToArray();
        }

        private static string? ResolveIcon(string pluginDirectory, string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
                return null;
            try
            {
                var root = Path.GetFullPath(pluginDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(
                    root,
                    icon.Replace('/', Path.DirectorySeparatorChar)));
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path)
                        ? path
                        : null;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    or IOException
                    or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
