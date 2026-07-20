using System.IO;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// Keeps a Web plugin's top-level document and bridge messages inside its own directory.
    /// This is independent from WebView2 so the trust boundary can be unit tested.
    /// </summary>
    public sealed class WebPluginNavigationPolicy
    {
        private readonly string _pluginRoot;

        public WebPluginNavigationPolicy(string pluginDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
            _pluginRoot = Path.GetFullPath(pluginDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        public bool IsTrustedLocalUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile)
                return false;

            try
            {
                var localPath = Path.GetFullPath(uri.LocalPath);
                return localPath.StartsWith(_pluginRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public bool TryResolveEntryPoint(string entryPoint, out Uri? entryUri)
        {
            entryUri = null;
            if (string.IsNullOrWhiteSpace(entryPoint)) return false;

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(_pluginRoot, entryPoint));
                var uri = new Uri(candidate);
                if (!IsTrustedLocalUri(uri.AbsoluteUri) || !File.Exists(candidate)) return false;
                entryUri = uri;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
