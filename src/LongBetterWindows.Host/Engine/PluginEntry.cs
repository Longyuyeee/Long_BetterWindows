using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Host.Engine
{
    public sealed class PluginEntry
    {
        public string Id => Manifest.Id;
        public PluginManifest Manifest { get; }
        public ILongPlugin Instance { get; }
        public PluginState State { get; set; } = PluginState.Loaded;
        public string Directory { get; }

        public PluginEntry(PluginManifest manifest, ILongPlugin instance, string directory)
        {
            Manifest = manifest;
            Instance = instance;
            Directory = directory;
        }

        public bool HasCapability(string capability)
            => Manifest.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
    }
}
