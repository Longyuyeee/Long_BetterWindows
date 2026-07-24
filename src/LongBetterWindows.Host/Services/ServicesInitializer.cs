using System.IO;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

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
        public static SparsePackageService SparsePackage { get; private set; } = null!;
        private static I18nService? _i18n;
        public static I18nService I18n
        {
            get
            {
                if (_i18n is not null) return _i18n;
                var service = new I18nService();
                service.Initialize(I18nService.DefaultLanguage);
                _i18n = service;
                return service;
            }
            private set => _i18n = value;
        }
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
        public static PowerService Power { get; private set; } = null!;
        public static ThemeService Theme { get; private set; } = null!;
        public static WallpaperService Wallpaper { get; private set; } = null!;
        public static BrightnessService Brightness { get; private set; } = null!;
        public static NetworkMonitorService NetworkMonitor { get; private set; } = null!;
        public static ContextCaptureService ContextCapture { get; private set; } = null!;
        public static SearchCoordinator Search { get; private set; } = null!;
        public static CommandWorkflowRepository Workflows { get; private set; } = null!;
        public static CommandWorkflowTemplateCatalog WorkflowTemplates { get; private set; } = null!;
        public static string WorkflowReportsDirectory { get; private set; } = string.Empty;
        public static SearchPreferenceService SearchPreferences { get; private set; } = null!;
        public static SuperPanelGroupService SuperPanelGroups { get; private set; } = null!;
        public static MouseGestureService MouseGestures { get; private set; } = null!;

        public static void Initialize(string? workflowsDirectory = null)
        {
            var provider = HostProvider.Instance;

            Storage = new StorageService();
            provider.RegisterService<IStorageService>(Storage);
            SearchPreferences = new SearchPreferenceService(Storage);
            SearchPreferences.InitializeAsync().GetAwaiter().GetResult();
            SuperPanelGroups = new SuperPanelGroupService(Storage);
            SuperPanelGroups.InitializeAsync().GetAwaiter().GetResult();
            MouseGestures = new MouseGestureService(Storage);
            MouseGestures.InitializeAsync().GetAwaiter().GetResult();

            Rollback = new RollbackEngine();

            HotKey = new HotKeyService();
            provider.RegisterService<IHotKeyService>(HotKey);
            provider.PluginStore.AttachHostResourceReleaser(
                async pluginId => { await HotKey.UnregisterPluginAsync(pluginId); });

            Registry = new RegistryService(Rollback);
            provider.RegisterService<IRegistryService>(Registry);

            ADS = new ADSService(Rollback);
            provider.RegisterService<IADSService>(ADS);

            ShellSelection = new ShellSelectionService();
            provider.RegisterService<IShellSelectionService>(ShellSelection);

            ColumnInjection = new ColumnInjectionService();

            ContextMenu = new ContextMenuService();
            SparsePackage = new SparsePackageService();
            I18n = new I18nService();

            Clipboard = new ClipboardService();
            provider.RegisterService<IClipboardService>(Clipboard);

            ContextCapture = new ContextCaptureService(new IContextProvider[]
            {
                new ExplorerContextProvider(ShellSelection),
                new ClipboardImageContextProvider(Clipboard),
                new ClipboardContextProvider(Clipboard),
            });
            var localDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LongBetterWindows");
            var workflowRoot = string.IsNullOrWhiteSpace(workflowsDirectory)
                ? Path.Combine(localDataRoot, "Workflows")
                : Path.GetFullPath(workflowsDirectory);
            WorkflowReportsDirectory = string.IsNullOrWhiteSpace(workflowsDirectory)
                ? Path.Combine(localDataRoot, "WorkflowReports")
                : Path.Combine(workflowRoot, ".reports");
            Workflows = new CommandWorkflowRepository(
                workflowRoot,
                "local-managed");
            WorkflowTemplates = new CommandWorkflowTemplateCatalog(
                Path.Combine(AppContext.BaseDirectory, "WorkflowTemplates"),
                Workflows);
            Search = new SearchCoordinator(
                new ISearchProvider[]
                {
                    new StaticCommandSearchProvider(provider.PluginStore.Commands),
                    new ManagedWorkflowSearchProvider(
                        provider.PluginStore,
                        Workflows,
                        key => I18n.T(key)),
                    new WindowsSettingsSearchProvider(),
                    new LocalFileSearchProvider(
                        localize: key => I18n.T(key)),
                },
                preferences: SearchPreferences);
            provider.PluginStore.AttachSearchCoordinator(Search);

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

            Power = new PowerService();
            provider.RegisterService<IPowerService>(Power);

            Theme = new ThemeService();
            provider.RegisterService<IThemeService>(Theme);

            Wallpaper = new WallpaperService();
            provider.RegisterService<IWallpaperService>(Wallpaper);

            Brightness = new BrightnessService();
            provider.RegisterService<IBrightnessService>(Brightness);

            NetworkMonitor = new NetworkMonitorService();
            provider.RegisterService<INetworkMonitorService>(NetworkMonitor);

            Startup = new StartupService();
        }

        public static void DisposeAll()
        {
            MouseGestures?.Dispose();
            (HotKey as IDisposable)?.Dispose();
            (Http as IDisposable)?.Dispose();
            (Storage as IDisposable)?.Dispose();
        }
    }
}
