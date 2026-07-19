using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Core;
using Serilog;

namespace FullPlugin;

/// <summary>
/// 全功能插件模板。
/// 多能力 (热键+Shell+存储) + 自定义设置 UI + 配置持久化。
/// 适合：需要完整交互和用户配置的复杂插件。
/// </summary>
public class FullPluginImpl : ILongPlugin, IHasSettingsUI
{
    private IHostApi? _host;
    private string _hotkey;

    public string Id => "com.example.full";
    public string Name => "全功能插件模板";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public FullPluginImpl()
    {
        _hotkey = "Ctrl+Shift+X"; // 默认值
    }

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;

        // 验证所有需要的能力
        if (!host.HasCapability("system.hotkey"))
            Log.Warning("[FullPlugin] system.hotkey 未授权");
        if (!host.HasCapability("shell.selection"))
            Log.Warning("[FullPlugin] shell.selection 未授权");
        if (!host.HasCapability("storage.local"))
            Log.Warning("[FullPlugin] storage.local 未授权");

        // 从持久化配置读取热键设置
        var savedHotkey = host.Storage?.GetAsync("hotkey").Result;
        if (savedHotkey is { IsSuccess: true, Data: not null })
            _hotkey = savedHotkey.Data;

        Log.Information("[FullPlugin] 初始化完成，热键: {Hotkey}", _hotkey);
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        if (_host?.HotKey == null) return false;

        var result = await _host.HotKey.RegisterAsync(_hotkey, OnHotkeyTriggered);
        if (!result.IsSuccess) return false;

        State = PluginState.Running;
        Log.Information("[FullPlugin] 已启动");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        if (_host?.HotKey != null)
            await _host.HotKey.UnregisterAsync(_hotkey);

        State = PluginState.Stopped;
        return true;
    }

    // ===== 自定义设置 UI =====
    // 实现 IHasSettingsUI → ToolCenter 显示齿轮按钮
    public FrameworkElement CreateSettingsUI()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "快捷键",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        });

        var hotkeyBox = new TextBox
        {
            Text = _hotkey,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 12),
        };
        hotkeyBox.TextChanged += (_, _) =>
        {
            _hotkey = hotkeyBox.Text.Trim();
            _host?.Storage?.SetAsync("hotkey", _hotkey);
        };
        panel.Children.Add(hotkeyBox);

        panel.Children.Add(new TextBlock
        {
            Text = "提示：修改后需重新启用插件生效。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        });

        return panel;
    }

    // ===== 业务逻辑 =====
    private void OnHotkeyTriggered()
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            // 示例：获取 Explorer 选中文件
            if (_host?.ShellSelection != null)
            {
                var result = await _host.ShellSelection
                    .GetActiveExplorerFolderPathAsync();

                var folder = result.IsSuccess ? result.Data ?? "无" : "未找到";
                MessageBox.Show($"当前文件夹: {folder}", Name);
            }
        });
    }
}
