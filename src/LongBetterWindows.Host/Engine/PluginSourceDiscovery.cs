using System.IO;

namespace LongBetterWindows.Host.Engine
{
    internal sealed record PluginSourceSnapshot(
        IReadOnlyList<string> PluginDirectories,
        IReadOnlyList<string> StandaloneScripts);

    internal sealed class PluginSourceDiscovery
    {
        private static readonly HashSet<string> StandaloneScriptExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".csx", ".js", ".ts" };
        private readonly IReadOnlyList<string> _scanDirectories;

        public PluginSourceDiscovery(string? pluginsDirectory = null)
        {
            var primary = Path.GetFullPath(pluginsDirectory ?? Path.Combine(
                AppContext.BaseDirectory, "Plugins"));
            var directories = new List<string> { primary };

            // Explicit directories are isolated for release verification and diagnostics.
            var development = pluginsDirectory is null ? FindDevelopmentPluginsDirectory() : null;
            if (development is not null
                && !directories.Contains(development, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(development);
            }

            foreach (var directory in directories)
                Directory.CreateDirectory(directory);

            _scanDirectories = directories;
        }

        public IReadOnlyList<string> ScanDirectories => _scanDirectories;

        public PluginSourceSnapshot Discover()
        {
            var pluginDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var standaloneScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scanDirectory in _scanDirectories)
            {
                if (!Directory.Exists(scanDirectory))
                    continue;

                foreach (var directory in Directory.EnumerateDirectories(scanDirectory))
                {
                    if (!Path.GetFileName(directory).StartsWith(
                        ".long_temp_", StringComparison.OrdinalIgnoreCase))
                    {
                        pluginDirectories.Add(Path.GetFullPath(directory));
                    }
                }

                foreach (var file in Directory.EnumerateFiles(
                    scanDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (StandaloneScriptExtensions.Contains(Path.GetExtension(file)))
                        standaloneScripts.Add(Path.GetFullPath(file));
                }
            }

            return new PluginSourceSnapshot(
                pluginDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                standaloneScripts.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList());
        }

        public bool IsStandaloneScript(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!StandaloneScriptExtensions.Contains(Path.GetExtension(fullPath)))
                return false;

            var parent = Path.GetDirectoryName(fullPath)?.TrimEnd(Path.DirectorySeparatorChar);
            return _scanDirectories.Any(directory => string.Equals(
                directory.TrimEnd(Path.DirectorySeparatorChar),
                parent,
                StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsPluginFile(string path)
        {
            if (path.Contains(
                $"{Path.DirectorySeparatorChar}.long_temp_",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            var name = Path.GetFileName(path);
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || StandaloneScriptExtensions.Contains(extension)
                || name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        public static string? FindPluginRootDirectory(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            for (var depth = 0; depth < 3 && directory is not null; depth++)
            {
                if (File.Exists(Path.Combine(directory, "manifest.json")))
                    return directory;

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        private static string? FindDevelopmentPluginsDirectory()
        {
            try
            {
                var directory = AppContext.BaseDirectory;
                for (var depth = 0; depth < 5; depth++)
                {
                    var parent = Directory.GetParent(directory);
                    if (parent is null)
                        break;

                    directory = parent.FullName;
                    var pluginsDirectory = Path.Combine(directory, "Plugins");
                    if (Directory.Exists(pluginsDirectory))
                        return Path.GetFullPath(pluginsDirectory);
                }
            }
            catch (IOException)
            {
                // Development discovery is optional.
            }
            catch (UnauthorizedAccessException)
            {
                // Development discovery is optional.
            }

            return null;
        }
    }
}
