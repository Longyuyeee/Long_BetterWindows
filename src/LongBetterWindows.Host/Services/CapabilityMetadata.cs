using System.Collections.Generic;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 能力元数据 - 提供每个能力的图标、名称、描述、安全级别
    /// </summary>
    public static class CapabilityMetadata
    {
        private static readonly Dictionary<string, CapabilityInfo> _metadata = new()
        {
            ["system.clipboard"] = new("📋", "剪贴板", "读写系统剪贴板内容", SecurityLevel.Safe),
            ["fs.ads.access"] = new("🗂️", "备用数据流", "读写文件的 NTFS 备用数据流", SecurityLevel.Medium),
            ["system.hotkey"] = new("⌨️", "全局热键", "注册全局快捷键", SecurityLevel.Safe),
            ["system.registry.read"] = new("🔍", "注册表读取", "读取注册表键值", SecurityLevel.Medium),
            ["system.registry.write"] = new("⚠️", "注册表写入", "修改注册表键值", SecurityLevel.High),
            ["network.http"] = new("🌐", "网络请求", "发起 HTTP/HTTPS 请求", SecurityLevel.Medium),
            ["shell.selection"] = new("📁", "文件选择", "获取资源管理器选中的文件", SecurityLevel.Safe),
            ["shell.execute"] = new("🚀", "执行命令", "打开文件、URL、执行命令", SecurityLevel.Medium),
            ["system.screenshot"] = new("📸", "截图", "捕获屏幕或窗口截图", SecurityLevel.Safe),
            ["system.window"] = new("🪟", "窗口操作", "获取和操作窗口信息", SecurityLevel.Safe),
            ["system.storage"] = new("💾", "本地存储", "读写插件配置和数据", SecurityLevel.Safe),
            ["ui.window"] = new("🖼️", "自定义窗口", "创建自定义 UI 窗口", SecurityLevel.Safe),
            ["ui.toast"] = new("💬", "通知", "显示 Toast 通知", SecurityLevel.Safe),
        };

        public static CapabilityInfo GetInfo(string capability)
        {
            return _metadata.TryGetValue(capability, out var info)
                ? info
                : new CapabilityInfo("❓", capability, "未知能力", SecurityLevel.Unknown);
        }

        public static Dictionary<string, CapabilityInfo> GetAll() => new(_metadata);
    }

    public record CapabilityInfo(
        string Icon,
        string DisplayName,
        string Description,
        SecurityLevel Level
    );

    public enum SecurityLevel
    {
        Safe,      // 安全 - 绿色
        Medium,    // 中等 - 黄色
        High,      // 高危 - 红色
        Unknown    // 未知 - 灰色
    }
}
