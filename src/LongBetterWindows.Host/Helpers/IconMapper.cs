using System.Collections.Generic;

namespace LongBetterWindows.Host.Helpers;

/// <summary>
/// 图标映射辅助类 - 为能力 API 提供图标
/// </summary>
public static class IconMapper
{
    private static readonly Dictionary<string, string> _capabilityIcons = new()
    {
        // 13 种能力 API 图标
        { "clipboard", "📋" },
        { "hotkey", "⌨️" },
        { "registry", "📝" },
        { "http", "🌐" },
        { "screenshot", "📸" },
        { "storage", "💾" },
        { "shell", "💻" },
        { "ads", "📎" },
        { "notification", "🔔" },
        { "theme", "🎨" },
        { "window", "🪟" },
        { "fs.ads.access", "📎" },
        { "system.registry.write", "📝" },
        { "network.http", "🌐" },
        { "system.hotkey", "⌨️" },
        { "system.screenshot", "📸" },
        { "system.storage", "💾" },
        { "system.shell.execute", "💻" },
        { "system.clipboard", "📋" },
        { "system.notification", "🔔" },
        { "system.theme", "🎨" },
        { "system.window", "🪟" }
    };

    private static readonly Dictionary<string, string> _runtimeIcons = new()
    {
        { "dotnet", "🔷" },
        { "webview", "🌐" },
        { "csharp-script", "📜" }
    };

    private static readonly Dictionary<string, string> _statusIcons = new()
    {
        { "running", "▶️" },
        { "stopped", "⏸️" },
        { "error", "❌" },
        { "loading", "⏳" }
    };

    /// <summary>
    /// 获取能力图标
    /// </summary>
    public static string GetCapabilityIcon(string capability)
    {
        return _capabilityIcons.TryGetValue(capability, out var icon) ? icon : "🔧";
    }

    /// <summary>
    /// 获取运行时图标
    /// </summary>
    public static string GetRuntimeIcon(string runtime)
    {
        return _runtimeIcons.TryGetValue(runtime, out var icon) ? icon : "📦";
    }

    /// <summary>
    /// 获取状态图标
    /// </summary>
    public static string GetStatusIcon(string status)
    {
        return _statusIcons.TryGetValue(status, out var icon) ? icon : "⚪";
    }

    /// <summary>
    /// 获取 Toast 类型图标
    /// </summary>
    public static string GetToastIcon(string type)
    {
        return type.ToLower() switch
        {
            "success" => "✅",
            "error" => "❌",
            "warning" => "⚠️",
            "info" => "ℹ️",
            _ => "ℹ️"
        };
    }

    /// <summary>
    /// 获取文件类型图标
    /// </summary>
    public static string GetFileTypeIcon(string extension)
    {
        return extension.ToLower() switch
        {
            ".csx" => "📜",
            ".js" => "🟨",
            ".ts" => "🔷",
            ".lpak" => "📦",
            ".dll" => "🔷",
            ".html" => "🌐",
            ".json" => "📋",
            ".xml" => "📄",
            _ => "📄"
        };
    }
}
