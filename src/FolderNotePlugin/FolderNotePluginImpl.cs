using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Views;
using Serilog;

namespace FolderNotePlugin;

public class FolderNotePluginImpl : ILongPlugin, IHasSettingsUI
{
    private IHostApi _host = null!;
    private IHotKeyService _hotKey = null!;

    public string Id => "com.long.folder-note";
    public string Name => "文件夹备注助手";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _hotKey = host.HotKey; // 若无 system.hotkey 能力会抛出 UnauthorizedAccessException

        Log.Information("[FolderNotePlugin] 初始化完成");
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var result = await _hotKey.RegisterAsync("Alt+M", OnHotkeyTriggered);

        if (!result.IsSuccess)
        {
            Log.Error("[FolderNotePlugin] 热键注册失败: {Error}", result.ErrorMessage);
            State = PluginState.Error;
            return false;
        }

        State = PluginState.Running;
        Log.Information("[FolderNotePlugin] 已启动，热键 Alt+M");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _hotKey.UnregisterAsync("Alt+M");

        State = PluginState.Disabled;
        Log.Information("[FolderNotePlugin] 已停止");
        return true;
    }

    private bool _isActive;

    private async void OnHotkeyTriggered()
    {
        if (_isActive) return;
        _isActive = true;

        try
        {
            await ShowNoteHudAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FolderNotePlugin] 执行失败");
        }
        finally
        {
            _isActive = false;
        }
    }

    private async Task ShowNoteHudAsync()
    {
        var shell = _host.ShellSelection;
        var ads = _host.ADS;

        var folderResult = await shell.GetActiveExplorerFolderPathAsync();
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            FloatingHudWindow.ShowToast("请先打开资源管理器并选中文件夹。");
            return;
        }

        var folderPath = folderResult.Data;
        var noteResult = await ads.ReadAsync(folderPath, "long_note");

        string? existingNote = null;
        if (noteResult.IsSuccess && noteResult.Data != null)
            existingNote = noteResult.Data;

        var rectResult = await shell.GetSelectedItemScreenRectAsync();
        double hudX, hudY;

        if (rectResult.IsSuccess)
        {
            hudX = rectResult.Data.X;
            hudY = rectResult.Data.Y + rectResult.Data.Height + 8;
        }
        else
        {
            GetCursorPos(out var pt);
            hudX = pt.X;
            hudY = pt.Y;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            FloatingHudWindow.ShowAt(hudX, hudY, existingNote, folderPath,
                async (text) =>
                {
                    if (string.IsNullOrEmpty(text))
                        await ads.DeleteAsync(folderPath, "long_note");
                    else
                        await ads.WriteAsync(folderPath, "long_note", text);

                    Log.Information("[FolderNotePlugin] 备注已保存: {Path}", folderPath);
                });
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "文件夹备注助手", Id, "Alt+M",
            newHotkey =>
            {
                // 热键变更时的回调——由 HotkeySettingsControl 内部处理
            });
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
