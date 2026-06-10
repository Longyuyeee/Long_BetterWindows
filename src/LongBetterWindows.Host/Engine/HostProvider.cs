using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class HostProvider : IHostApi
    {
        private static readonly Lazy<HostProvider> _instance = new(() => new HostProvider());
        public static HostProvider Instance => _instance.Value;

        private readonly PluginRegistry _registry;
        private readonly Dictionary<Type, object> _services = new();

        private HostProvider()
        {
            _registry = new PluginRegistry();
        }

        public PluginRegistry PluginStore => _registry;

        public void RegisterService<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
            Log.Debug("服务 {ServiceType} 已注册", typeof(T).Name);
        }

        public void UnregisterService<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        private T? GetService<T>() where T : class
        {
            var pluginId = PluginAccessContext.CurrentPluginId;
            if (pluginId == null)
            {
                Log.Warning("尝试在插件上下文之外访问服务 {ServiceType}", typeof(T).Name);
                return null;
            }

            var capability = GetCapabilityForService<T>();
            if (capability != null && !_registry.HasCapability(pluginId, capability))
            {
                Log.Warning("插件 {PluginId} 未声明能力 {Capability}", pluginId, capability);
                return null;
            }

            if (_services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            Log.Debug("服务 {ServiceType} 未注册（将在阶段四实现）", typeof(T).Name);
            return null;
        }

        private static string? GetCapabilityForService<T>() where T : class
        {
            if (typeof(T) == typeof(IHotKeyService))
                return "system.hotkey";
            if (typeof(T) == typeof(IShellSelectionService))
                return "shell.selection";
            if (typeof(T) == typeof(IADSService))
                return "fs.ads.access";
            if (typeof(T) == typeof(IRegistryService))
                return "system.registry.write";
            if (typeof(T) == typeof(IStorageService))
                return "storage.local";
            return null;
        }

        public IHotKeyService? HotKey => GetService<IHotKeyService>();
        public IShellSelectionService? ShellSelection => GetService<IShellSelectionService>();
        public IADSService? ADS => GetService<IADSService>();
        public IRegistryService? Registry => GetService<IRegistryService>();
        public IStorageService? Storage => GetService<IStorageService>();
    }
}
