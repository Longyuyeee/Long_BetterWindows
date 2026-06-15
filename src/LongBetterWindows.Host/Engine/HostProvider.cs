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
        private static readonly AsyncLocal<string?> _lastAccessError = new();

        private HostProvider()
        {
            _registry = new PluginRegistry();
        }

        public PluginRegistry PluginStore => _registry;

        /// <summary>最后一次服务访问被拒绝的原因（权限不足时非 null）</summary>
        public string? LastAccessError => _lastAccessError.Value;

        /// <summary>检查当前插件是否声明了指定能力</summary>
        public bool HasCapability(string capability)
        {
            var pluginId = PluginAccessContext.CurrentPluginId;
            if (pluginId == null) return true; // 内置调用，不受限制
            return _registry.HasCapability(pluginId, capability);
        }

        public void RegisterService<T>(T service) where T : class
        {
            _services[typeof(T)] = service;
            Log.Debug("服务 {ServiceType} 已注册", typeof(T).Name);
        }

        public void UnregisterService<T>() where T : class
        {
            _services.Remove(typeof(T));
        }

        private T GetService<T>() where T : class
        {
            _lastAccessError.Value = null; // 每次访问前清除旧错误
            var pluginId = PluginAccessContext.CurrentPluginId;

            // 内置工具（无插件上下文）：直接返回已注册服务
            if (pluginId == null)
            {
                if (_services.TryGetValue(typeof(T), out var builtinService))
                {
                    return (T)builtinService;
                }

                throw new InvalidOperationException(
                    $"服务 {typeof(T).Name} 未注册，请检查 ServicesInitializer。");
            }

            // 外部插件：校验 manifest.capabilities 权限
            var capability = GetCapabilityForService<T>();
            if (capability != null && !_registry.HasCapability(pluginId, capability))
            {
                var msg = $"插件 '{pluginId}' 未声明能力 '{capability}'，" +
                          $"请在 manifest.json 的 capabilities 数组中添加 \"{capability}\"";
                _lastAccessError.Value = msg;
                Log.Warning("插件 {PluginId} 未声明能力 {Capability}，拒绝访问 {ServiceType}",
                    pluginId, capability, typeof(T).Name);
                throw new UnauthorizedAccessException(msg);
            }

            if (_services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            throw new InvalidOperationException(
                $"服务 {typeof(T).Name} 未注册，请检查 ServicesInitializer。");
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
            if (typeof(T) == typeof(IClipboardService))
                return "system.clipboard";
            if (typeof(T) == typeof(INotificationService))
                return "system.notification";
            if (typeof(T) == typeof(IFileOpsService))
                return "file.ops";
            if (typeof(T) == typeof(IWindowInfoService))
                return "window.info";
            if (typeof(T) == typeof(IScreenCaptureService))
                return "system.screenshot";
            if (typeof(T) == typeof(IInputService))
                return "system.input";
            if (typeof(T) == typeof(IProcessService))
                return "system.process";
            if (typeof(T) == typeof(IHttpService))
                return "network.http";
            if (typeof(T) == typeof(IShellExecuteService))
                return "shell.execute";
            return null;
        }

        public IHotKeyService HotKey => GetService<IHotKeyService>();
        public IShellSelectionService ShellSelection => GetService<IShellSelectionService>();
        public IADSService ADS => GetService<IADSService>();
        public IRegistryService Registry => GetService<IRegistryService>();
        public IStorageService Storage => GetService<IStorageService>();
        public IClipboardService Clipboard => GetService<IClipboardService>();
        public INotificationService Notification => GetService<INotificationService>();
        public IFileOpsService FileOps => GetService<IFileOpsService>();
        public IWindowInfoService WindowInfo => GetService<IWindowInfoService>();
        public IScreenCaptureService ScreenCapture => GetService<IScreenCaptureService>();
        public IInputService Input => GetService<IInputService>();
        public IProcessService Process => GetService<IProcessService>();
        public IHttpService Http => GetService<IHttpService>();
        public IShellExecuteService ShellExecute => GetService<IShellExecuteService>();
    }
}
