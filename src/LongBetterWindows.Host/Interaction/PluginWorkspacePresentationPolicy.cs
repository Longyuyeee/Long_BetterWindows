using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal enum PluginSurfaceOwnership
    {
        HostWorkspaceSession,
        PluginOwned,
    }

    internal static class PluginWorkspacePresentationPolicy
    {
        public static PluginSurfaceOwnership Resolve(PluginManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            return PluginRuntimeLoader.GetRuntimeKind(manifest.Runtime)
                == PluginRuntimeKind.WebView
                    ? PluginSurfaceOwnership.HostWorkspaceSession
                    : PluginSurfaceOwnership.PluginOwned;
        }
    }
}
