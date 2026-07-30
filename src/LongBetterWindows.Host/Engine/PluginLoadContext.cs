using System.Reflection;
using System.Runtime.Loader;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;

namespace LongBetterWindows.Host.Engine
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        internal static IReadOnlyList<Assembly> SharedSdkAssemblies { get; } =
        [
            typeof(ILongPlugin).Assembly,
            typeof(HotkeySettingsControl).Assembly,
        ];
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDir;

        public PluginLoadContext(string pluginDir)
            : base(isCollectible: true)
        {
            _pluginDir = pluginDir;
            _resolver = new AssemblyDependencyResolver(pluginDir);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var sharedAssembly = SharedSdkAssemblies.FirstOrDefault(candidate =>
                string.Equals(
                    assemblyName.Name,
                    candidate.GetName().Name,
                    StringComparison.OrdinalIgnoreCase));
            if (sharedAssembly is not null)
            {
                return sharedAssembly;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
