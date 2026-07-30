using System.IO;
using System.Security.Cryptography;
using System.Text;

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
            VirtualHostName = BuildVirtualHostName(_pluginRoot);
        }

        public string PluginRoot => _pluginRoot;
        public string VirtualHostName { get; }

        public const string DefaultContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data:; font-src 'self'; connect-src 'none'; "
            + "object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'";

        public bool IsTrustedLocalUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return false;

            if (IsTrustedVirtualUri(uri))
                return true;

            if (!uri.IsFile)
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

        public string BuildContentSecurityPolicyInjectionScript()
        {
            var policy = System.Text.Json.JsonSerializer.Serialize(
                DefaultContentSecurityPolicy);
            return $$"""
                (function () {
                  const installLongCsp = function () {
                    if (document.querySelector('meta[http-equiv="Content-Security-Policy"]')) return;
                    const meta = document.createElement('meta');
                    meta.httpEquiv = 'Content-Security-Policy';
                    meta.content = {{policy}};
                    (document.head || document.documentElement).prepend(meta);
                  };
                  if (document.readyState === 'loading')
                    document.addEventListener('DOMContentLoaded', installLongCsp, { once: true });
                  else
                    installLongCsp();
                })();
                """;
        }

        public bool TryResolveEntryPoint(string entryPoint, out Uri? entryUri)
        {
            entryUri = null;
            if (string.IsNullOrWhiteSpace(entryPoint)) return false;

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(_pluginRoot, entryPoint));
                if (!IsLocalPathInsidePluginRoot(candidate) || !File.Exists(candidate))
                    return false;

                var relativePath = Path.GetRelativePath(_pluginRoot, candidate)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                entryUri = new Uri($"https://{VirtualHostName}/{Uri.EscapeDataString(relativePath).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsTrustedWebViewUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return false;

            return IsTrustedVirtualUri(uri);
        }

        private bool IsLocalPathInsidePluginRoot(string path)
        {
            try
            {
                var localPath = Path.GetFullPath(path);
                return localPath.StartsWith(_pluginRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsTrustedVirtualUri(Uri uri)
        {
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !uri.Host.Equals(VirtualHostName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return uri.IsDefaultPort || uri.Port == 443;
        }

        private static string BuildVirtualHostName(string pluginRoot)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pluginRoot.ToUpperInvariant()));
            var builder = new StringBuilder("plugin-", 16 + ".longplugin.local".Length);
            for (var index = 0; index < 8; index++)
                builder.Append(bytes[index].ToString("x2"));
            builder.Append(".longplugin.local");
            return builder.ToString();
        }
    }
}
