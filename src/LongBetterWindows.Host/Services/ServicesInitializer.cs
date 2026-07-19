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
        public static ClipboardService Clipboard { get; private set; } = null!;
        public static NotificationService Notification { get; private set; } = null!;
        public static FileOpsService FileOps { get; private set; } = null!;
        public static WindowInfoService WindowInfo { get; private set; } = null!;
        public static ScreenCaptureService ScreenCapture { get; private set; } = null!;
        public static InputService Input { get; private set; } = null!;
        public static ProcessService Process { get; private set; } = null!;
        public static HttpService Http { get; private set; } = null!;
        public static ShellExecuteService ShellExecute { get; private set; } = null!;
        public static StartupService Startup { get; private set; } = null!;
        public static ContextCaptureService ContextCapture { get; private set; } = null!;
        public static SearchCoordinator Search { get; private set; } = null!;
        public static SearchPreferenceService SearchPreferences { get; private set; } = null!;
        public static SuperPanelGroupService SuperPanelGroups { get; private set; } = null!;
        public static MouseGestureService MouseGestures { get; private set; } = null!;

        public static void Initialize()
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

            Clipboard = new ClipboardService();
            provider.RegisterService<IClipboardService>(Clipboard);

            ContextCapture = new ContextCaptureService(new IContextProvider[]
            {
                new ExplorerContextProvider(ShellSelection),
                new ClipboardImageContextProvider(Clipboard),
                new ClipboardContextProvider(Clipboard),
            });
            Search = new SearchCoordinator(
                new ISearchProvider[]
                {
                    new StaticCommandSearchProvider(provider.PluginStore.Commands),
                    new WindowsSettingsSearchProvider(),
                    new LocalFileSearchProvider(),
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

            // I18nService 预留，待国际化时启用
            // I18nService.Initialize();

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
