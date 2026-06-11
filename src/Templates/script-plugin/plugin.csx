// ============================================
// 脚本插件模板 (.csx)
// 保存此文件即生效，无需编译！
// ============================================
// 可用全局变量:
//   Host  - IHostApi (hotkey/clipboard/shell/ADS/storage/registry)
//   Id    - 插件 ID
//   Name  - 插件名称
//
// 可用委托:
//   Start - Func<Task>  插件启动时执行
//   Stop  - Func<Task>  插件停止时执行
// ============================================

// 启动：注册热键
Start = async () =>
{
    if (Host.HotKey != null)
    {
        await Host.HotKey.RegisterAsync("Alt+X", () =>
        {
            // 热键触发时的逻辑
            var clipboard = Host.Clipboard;
            if (clipboard != null)
            {
                var text = clipboard.GetTextAsync().Result;
                if (text.IsSuccess && text.Data != null)
                {
                    // 在这里处理剪贴板内容
                    Log.Information("[Script] 剪贴板: {Len} 字符", text.Data.Length);
                }
            }
        });

        Log.Information("[Script:{PluginId}] 热键 Alt+X 已注册", Id);
    }
};

// 停止：注销热键
Stop = async () =>
{
    if (Host.HotKey != null)
    {
        await Host.HotKey.UnregisterAsync("Alt+X");
    }
    Log.Information("[Script:{PluginId}] 已停止", Id);
};
