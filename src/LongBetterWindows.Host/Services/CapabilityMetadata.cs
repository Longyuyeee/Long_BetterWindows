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
            ["system.clipboard.monitor"] = new("📌", "剪贴板监听", "监听系统剪贴板内容变化", SecurityLevel.Medium),
            ["fs.ads.access"] = new("🗂️", "备用数据流", "读写文件的 NTFS 备用数据流", SecurityLevel.Medium),
            ["system.hotkey"] = new("⌨️", "全局热键", "注册全局快捷键", SecurityLevel.Safe),
            ["shell.context_menu"] = new("🖱️", "右键菜单", "向系统右键菜单添加插件命令", SecurityLevel.Medium),
            ["shell.ui.locator"] = new("🎯", "界面定位", "定位资源管理器等系统界面", SecurityLevel.Medium),
            ["ui.floating_box"] = new("✨", "悬浮组件", "创建轻量悬浮交互组件", SecurityLevel.Safe),
            ["system.registry.read"] = new("🔍", "注册表读取", "读取注册表键值", SecurityLevel.Medium),
            ["system.registry.write"] = new("⚠️", "注册表写入", "修改注册表键值", SecurityLevel.High),
            ["network.http"] = new("🌐", "网络请求", "发起 HTTP/HTTPS 请求", SecurityLevel.Medium),
            ["shell.selection"] = new("📁", "文件选择", "获取资源管理器选中的文件", SecurityLevel.Safe),
            ["shell.execute"] = new("🚀", "执行命令", "打开文件、URL、执行命令", SecurityLevel.Medium),
            ["system.screenshot"] = new("📸", "截图", "捕获屏幕或窗口截图", SecurityLevel.Safe),
            ["window.info"] = new("🪟", "窗口操作", "获取和操作窗口信息", SecurityLevel.Safe),
            ["storage.local"] = new("💾", "本地存储", "读写插件配置和数据", SecurityLevel.Safe),
            ["ui.window"] = new("🖼️", "自定义窗口", "创建自定义 UI 窗口", SecurityLevel.Safe),
            ["system.notification"] = new("💬", "通知", "显示 Toast 通知", SecurityLevel.Safe),
            ["system.input"] = new("🖱️", "输入模拟", "模拟键盘和鼠标输入", SecurityLevel.High),
            ["system.process"] = new("⚙️", "进程管理", "查看、启动和结束系统进程", SecurityLevel.High),
            ["file.ops"] = new("📂", "文件操作", "复制、移动和删除文件", SecurityLevel.Medium),
            ["network.ports"] = new("🔌", "端口管理", "查看和管理网络端口", SecurityLevel.Medium),
            ["network.monitor"] = new("📶", "网络监控", "读取实时网络流量", SecurityLevel.Safe),
            ["system.performance"] = new("📊", "性能监控", "读取系统性能数据", SecurityLevel.Safe),
            ["filesystem.advanced"] = new("🗃️", "高级文件系统", "执行高级文件系统操作", SecurityLevel.Medium),
            ["text.pinyin"] = new("拼", "拼音服务", "进行文本拼音转换和匹配", SecurityLevel.Safe),
            ["system.cache"] = new("🧹", "缓存管理", "查看和清理系统缓存", SecurityLevel.Medium),
            ["system.schedule"] = new("⏱️", "计划任务", "创建和管理定时任务", SecurityLevel.Medium),
            ["system.audio"] = new("🔊", "音频控制", "读取和调整系统音量", SecurityLevel.Medium),
            ["system.power"] = new("⏻", "电源管理", "执行关机、重启和休眠操作", SecurityLevel.High),
            ["system.theme"] = new("◐", "系统主题", "读取和切换系统主题", SecurityLevel.Medium),
            ["system.wallpaper"] = new("🏞️", "桌面壁纸", "读取和更换桌面壁纸", SecurityLevel.Medium),
            ["display.brightness"] = new("☀️", "屏幕亮度", "读取和调整显示器亮度", SecurityLevel.Medium),
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
