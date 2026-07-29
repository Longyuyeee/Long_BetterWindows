using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.PluginSdk.Testing;

public sealed class PluginTestHost : IHostApi
{
    private readonly Dictionary<Type, object> _services = new();
    private readonly HashSet<string> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);

    public string? LastAccessError { get; private set; }

    public IReadOnlyCollection<string> GrantedCapabilities =>
        _capabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray();

    public PluginTestHost GrantCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        _capabilities.Add(capability);
        return this;
    }

    public PluginTestHost Grant<TService>(
        TService service,
        params string[] additionalCapabilities)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(TService)] = service;

        var mappedCapability = HostCapabilityCatalog.ForService<TService>();
        if (mappedCapability is not null)
            _capabilities.Add(mappedCapability);
        foreach (var capability in additionalCapabilities)
            GrantCapability(capability);

        return this;
    }

    public PluginTestHost RevokeCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        _capabilities.Remove(capability);
        return this;
    }

    public bool HasCapability(string capability) =>
        _capabilities.Contains(capability);

    public IHotKeyService HotKey => Get<IHotKeyService>();
    public IPluginSettingsService Settings => Get<IPluginSettingsService>();
    public IShellSelectionService ShellSelection => Get<IShellSelectionService>();
    public IADSService ADS => Get<IADSService>();
    public IRegistryService Registry => Get<IRegistryService>();
    public IStorageService Storage => Get<IStorageService>();
    public IClipboardService Clipboard => Get<IClipboardService>();
    public INotificationService Notification => Get<INotificationService>();
    public IFileOpsService FileOps => Get<IFileOpsService>();
    public IWindowInfoService WindowInfo => Get<IWindowInfoService>();
    public IScreenCaptureService ScreenCapture => Get<IScreenCaptureService>();
    public IInputService Input => Get<IInputService>();
    public IProcessService Process => Get<IProcessService>();
    public IHttpService Http => Get<IHttpService>();
    public IShellExecuteService ShellExecute => Get<IShellExecuteService>();
    public IUICapability UI => Get<IUICapability>();
    public INetworkPortService NetworkPort => Get<INetworkPortService>();
    public IPerformanceService Performance => Get<IPerformanceService>();
    public IFileSystemService FileSystem => Get<IFileSystemService>();
    public IPinyinService Pinyin => Get<IPinyinService>();
    public ICacheService Cache => Get<ICacheService>();
    public IScheduleService Schedule => Get<IScheduleService>();
    public IAudioService Audio => Get<IAudioService>();
    public IPowerService Power => Get<IPowerService>();
    public IThemeService Theme => Get<IThemeService>();
    public IWallpaperService Wallpaper => Get<IWallpaperService>();
    public IBrightnessService Brightness => Get<IBrightnessService>();
    public INetworkMonitorService NetworkMonitor => Get<INetworkMonitorService>();

    private TService Get<TService>() where TService : class
    {
        LastAccessError = null;
        var capability = HostCapabilityCatalog.ForService<TService>();
        if (capability is not null && !HasCapability(capability))
        {
            LastAccessError =
                $"Test plugin has not declared capability '{capability}' " +
                $"required by {typeof(TService).Name}.";
            throw new UnauthorizedAccessException(LastAccessError);
        }

        if (_services.TryGetValue(typeof(TService), out var service))
            return (TService)service;

        throw new InvalidOperationException(
            $"No test double is registered for {typeof(TService).Name}. " +
            $"Call {nameof(Grant)}<{typeof(TService).Name}>(service) first.");
    }
}
