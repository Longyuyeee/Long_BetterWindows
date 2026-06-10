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

        public static void Initialize()
        {
            var provider = HostProvider.Instance;

            Storage = new StorageService();
            provider.RegisterService<IStorageService>(Storage);

            Rollback = new RollbackEngine();

            HotKey = new HotKeyService();
            provider.RegisterService<IHotKeyService>(HotKey);

            Registry = new RegistryService();
            provider.RegisterService<IRegistryService>(Registry);

            ADS = new ADSService();
            provider.RegisterService<IADSService>(ADS);

            ShellSelection = new ShellSelectionService();
            provider.RegisterService<IShellSelectionService>(ShellSelection);
        }
    }
}
