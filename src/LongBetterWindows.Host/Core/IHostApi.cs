using LongBetterWindows.Host.Capabilities;

namespace LongBetterWindows.Host.Core
{
    public interface IHostApi
    {
        /// <summary>最后一次服务访问被拒绝的原因（权限不足时非 null，供诊断用）</summary>
        string? LastAccessError { get; }

        /// <summary>检查插件是否声明了指定能力</summary>
        bool HasCapability(string capability);

        IHotKeyService HotKey { get; }
        IShellSelectionService ShellSelection { get; }
        IADSService ADS { get; }
        IRegistryService Registry { get; }
        IStorageService Storage { get; }
        IClipboardService Clipboard { get; }
        INotificationService Notification { get; }
        IFileOpsService FileOps { get; }
        IWindowInfoService WindowInfo { get; }
        IScreenCaptureService ScreenCapture { get; }
        IInputService Input { get; }
        IProcessService Process { get; }
        IHttpService Http { get; }
        IShellExecuteService ShellExecute { get; }
        IUICapability UI { get; }
        INetworkPortService NetworkPort { get; }
        IPerformanceService Performance { get; }
        IFileSystemService FileSystem { get; }
        IPinyinService Pinyin { get; }
        ICacheService Cache { get; }
        IScheduleService Schedule { get; }
        IAudioService Audio { get; }
        IPowerService Power { get; }
        IThemeService Theme { get; }
        IWallpaperService Wallpaper { get; }
        IBrightnessService Brightness { get; }
        INetworkMonitorService NetworkMonitor { get; }
    }
}
