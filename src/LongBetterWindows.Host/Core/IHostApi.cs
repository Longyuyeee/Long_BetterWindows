using LongBetterWindows.Host.Capabilities;

namespace LongBetterWindows.Host.Core
{
    public interface IHostApi
    {
        IHotKeyService? HotKey { get; }
        IShellSelectionService? ShellSelection { get; }
        IADSService? ADS { get; }
        IRegistryService? Registry { get; }
        IStorageService? Storage { get; }
        IClipboardService? Clipboard { get; }
        INotificationService? Notification { get; }
    }
}
