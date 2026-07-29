using LongBetterWindows.Host.Capabilities;

namespace LongBetterWindows.Host.Core;

public static class HostCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<Type, string> ServiceCapabilities =
        new Dictionary<Type, string>
        {
            [typeof(IHotKeyService)] = "system.hotkey",
            [typeof(IPluginSettingsService)] = "storage.local",
            [typeof(IShellSelectionService)] = "shell.selection",
            [typeof(IADSService)] = "fs.ads.access",
            [typeof(IRegistryService)] = "system.registry.write",
            [typeof(IStorageService)] = "storage.local",
            [typeof(IClipboardService)] = "system.clipboard",
            [typeof(INotificationService)] = "system.notification",
            [typeof(IFileOpsService)] = "file.ops",
            [typeof(IWindowInfoService)] = "window.info",
            [typeof(IScreenCaptureService)] = "system.screenshot",
            [typeof(IScreenColorSampler)] = "system.screenshot",
            [typeof(IInputService)] = "system.input",
            [typeof(IProcessService)] = "system.process",
            [typeof(IHttpService)] = "network.http",
            [typeof(IShellExecuteService)] = "shell.execute",
            [typeof(IUICapability)] = "ui.window",
            [typeof(INetworkPortService)] = "network.ports",
            [typeof(IPerformanceService)] = "system.performance",
            [typeof(IFileSystemService)] = "filesystem.advanced",
            [typeof(IPinyinService)] = "text.pinyin",
            [typeof(ICacheService)] = "system.cache",
            [typeof(IScheduleService)] = "system.schedule",
            [typeof(IAudioService)] = "system.audio",
            [typeof(IPowerService)] = "system.power",
            [typeof(IThemeService)] = "system.theme",
            [typeof(IWallpaperService)] = "system.wallpaper",
            [typeof(IBrightnessService)] = "display.brightness",
            [typeof(INetworkMonitorService)] = "network.monitor",
        };

    public static IReadOnlyDictionary<Type, string> ServiceMap =>
        ServiceCapabilities;

    public static string? ForService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return ServiceCapabilities.GetValueOrDefault(serviceType);
    }

    public static string? ForService<T>() where T : class =>
        ForService(typeof(T));
}
