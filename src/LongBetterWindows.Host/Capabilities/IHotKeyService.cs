using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IHotKeyService
    {
        /// <summary>注册热键（用于插件，自动获取调用者 ID）</summary>
        Task<HostApiResponse> RegisterAsync(string hotkey, Action callback);

        /// <summary>注册热键（显式指定插件 ID）</summary>
        Task<HostApiResponse> RegisterAsync(string hotkey, string pluginId, Action callback);

        /// <summary>注销热键</summary>
        Task<HostApiResponse> UnregisterAsync(string hotkey);

        /// <summary>检测热键是否冲突</summary>
        Task<HostApiResponse<bool>> IsConflictAsync(string hotkey);

        /// <summary>检测热键是否冲突，可排除正在被替换的当前热键</summary>
        Task<HostApiResponse<bool>> IsConflictAsync(
            string hotkey,
            string? excludedHotkey);

        /// <summary>获取热键的拥有者插件 ID</summary>
        string? GetOwner(string hotkey);

        /// <summary>获取所有已注册的热键及其拥有者</summary>
        IReadOnlyDictionary<string, string> GetAllHotkeys();

        /// <summary>更换插件热键（原子操作：注销旧的+注册新的）</summary>
        Task<HostApiResponse> ChangeHotkeyAsync(string oldHotkey, string newHotkey, string pluginId, Action callback);
    }
}
