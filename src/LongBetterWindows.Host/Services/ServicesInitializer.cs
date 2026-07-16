using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Services
{
    public static class ServicesInitializer
    {
        public static StorageService Storage { get; private set; } = null!;
        public static HotKeyService HotKey { get; private set; } = null!;
        public static RegistryService Registry { get; private set; } = null!;
        public static RollbackEngine Rollback { get; private set; } = null!;
        public static ADSService ADS { get; private set; } = null!;
        public static ShellSelectionService ShellSelection { get; private set; } = null!;
        public static ColumnInjectionService ColumnInjection { get; private set; } = null!;
        public static ContextMenuService ContextMenu { get; private set; } = null!;
        public static ClipboardService Clipboard { get; private set; } = null!;
        public static NotificationService Notification { get; private set; } = null!;
        public static FileOpsService FileOps { get; private set; } = null!;
        public static WindowInfoService WindowInfo { get; private set; } = null!;
        public static ScreenCaptureService ScreenCapture { get; private set; } = null!;
        public static InputService Input { get; private set; } = null!;
        public static ProcessService Process { get; private set; } = null!;
        public static HttpService Http { get; private set; } = null!;
        public static ShellExecuteService ShellExecute { get; private set; } = null!;
        public static UIService UI { get; private set; } = null!;
        public static StartupService Startup { get; private set; } = null!;
        public static NetworkPortService NetworkPort { get; private set; } = null!;
        public static PerformanceService Performance { get; private set; } = null!;
        public static FileSystemService FileSystem { get; private set; } = null!;
        public static PinyinService Pinyin { get; private set; } = null!;
        public static CacheService Cache { get; private set; } = null!;
        public static ScheduleService Schedule { get; private set; } = null!;
        public static AudioService Audio { get; private set; } = null!;

        public static void Initialize()
        {
            var provider = HostProvider.Instance;

            Storage = new StorageService();
            provider.RegisterService<IStorageService>(Storage);

            Rollback = new RollbackEngine();

            HotKey = new HotKeyService();
            provider.RegisterService<IHotKeyService>(HotKey);

            Registry = new RegistryService(Rollback);
            provider.RegisterService<IRegistryService>(Registry);

            ADS = new ADSService(Rollback);
            provider.RegisterService<IADSService>(ADS);

            ShellSelection = new ShellSelectionService();
            provider.RegisterService<IShellSelectionService>(ShellSelection);

            ColumnInjection = new ColumnInjectionService();

            ContextMenu = new ContextMenuService();

            Clipboard = new ClipboardService();
            provider.RegisterService<IClipboardService>(Clipboard);

            Notification = new NotificationService();
            provider.RegisterService<INotificationService>(Notification);

            FileOps = new FileOpsService();
            provider.RegisterService<IFileOpsService>(FileOps);

            WindowInfo = new WindowInfoService();
            provider.RegisterService<IWindowInfoService>(WindowInfo);

            ScreenCapture = new ScreenCaptureService();
            provider.RegisterService<IScreenCaptureService>(ScreenCapture);

            Input = new InputService();
            provider.RegisterService<IInputService>(Input);

            Process = new ProcessService();
            provider.RegisterService<IProcessService>(Process);

            Http = new HttpService();
            provider.RegisterService<IHttpService>(Http);

            ShellExecute = new ShellExecuteService();
            provider.RegisterService<IShellExecuteService>(ShellExecute);

            UI = new UIService();
            provider.RegisterService<IUICapability>(UI);

            NetworkPort = new NetworkPortService();
            provider.RegisterService<INetworkPortService>(NetworkPort);

            Performance = new PerformanceService();
            provider.RegisterService<IPerformanceService>(Performance);

            FileSystem = new FileSystemService();
            provider.RegisterService<IFileSystemService>(FileSystem);

            Pinyin = new PinyinService();
            provider.RegisterService<IPinyinService>(Pinyin);

            Cache = new CacheService();
            provider.RegisterService<ICacheService>(Cache);

            Schedule = new ScheduleService();
            provider.RegisterService<IScheduleService>(Schedule);

            Audio = new AudioService();
            provider.RegisterService<IAudioService>(Audio);

            // I18nService 预留，待国际化时启用
            // I18nService.Initialize();

            Startup = new StartupService();
        }

        public static void DisposeAll()
        {
            (HotKey as IDisposable)?.Dispose();
            (Http as IDisposable)?.Dispose();
            (Storage as IDisposable)?.Dispose();
        }
    }
}
