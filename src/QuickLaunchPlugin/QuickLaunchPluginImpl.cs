using System.Diagnostics;
using System.IO;
using System.Windows;
using LongBetterWindows.Host.Core;
using Serilog;

namespace QuickLaunchPlugin;

public class QuickLaunchPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;
    private bool _isActive;
    private string _hotkey = "Ctrl+Shift+Space";

    public string Id => "com.long.quicklaunch";
    public string Name => "快捷启动器";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;

        if (!host.HasCapability("system.hotkey"))
        {
            Log.Error("[QuickLaunch] 未获得热键能力授权");
            State = PluginState.Error;
            return Task.FromResult(false);
        }

        Log.Information("[QuickLaunch] 初始化完成");
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var result = await _host!.HotKey!.RegisterAsync(
            "Ctrl+Shift+Space", OnHotkeyTriggered);

        if (!result.IsSuccess)
        {
            Log.Error("[QuickLaunch] 热键注册失败");
            State = PluginState.Error;
            return false;
        }

        State = PluginState.Running;
        Log.Information("[QuickLaunch] 已启动，热键 Ctrl+Shift+Space");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _host!.HotKey!.UnregisterAsync("Ctrl+Shift+Space");
        State = PluginState.Disabled;
        return true;
    }

    private void OnHotkeyTriggered()
    {
        if (_isActive) return;
        _isActive = true;

        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                LaunchWindow.Show(OnAppSelected);
            }
            finally
            {
                _isActive = false;
            }
        });
    }

    private static void OnAppSelected(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            Log.Information("[QuickLaunch] 启动: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuickLaunch] 启动失败: {Path}", path);
        }
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "快捷启动器", Id, _hotkey,
            newHotkey =>
            {
                _hotkey = newHotkey;
                _host?.Storage?.SetAsync("quicklaunch_hotkey", newHotkey);
            });
    }

    public void ShowMainUI()
    {
        Application.Current.Dispatcher.Invoke(() =>
            LaunchWindow.Show(path =>
            {
                if (!string.IsNullOrEmpty(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = path, UseShellExecute = true });
            }));
    }
}
