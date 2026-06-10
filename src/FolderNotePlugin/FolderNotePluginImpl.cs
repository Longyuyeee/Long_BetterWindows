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
    private IHostApi? _host;
    private IHotKeyService? _hotKey;

    public string Id => "com.long.folder-note";
    public string Name => "文件夹备注助手";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _hotKey = host.HotKey;

        if (_hotKey == null)
        {
            Log.Error("[FolderNotePlugin] 未获得热键能力授权");
            State = PluginState.Error;
            return Task.FromResult(false);
        }

        Log.Information("[FolderNotePlugin] 初始化完成");
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        if (_hotKey == null) return false;

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
        if (_hotKey != null)
            await _hotKey.UnregisterAsync("Alt+M");

        State = PluginState.Disabled;
        Log.Information("[FolderNotePlugin] 已停止");
        return true;
    }

    private bool _isActive;

    private async void OnHotkeyTriggered()
    {
        if (_isActive || _host == null) return;
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
        var shell = _host!.ShellSelection;
        var ads = _host.ADS;

        if (shell == null || ads == null)
        {
            FloatingHudWindow.ShowToast("所需能力未授权。");
            return;
        }

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
        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "文件夹备注助手",
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

        var hotkeyBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x7A, 0xFF)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 4, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "Alt + M",
                FontSize = 18,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF)),
            },
        };
        panel.Children.Add(hotkeyBadge);

        panel.Children.Add(new TextBlock
        {
            Text = "使用方法",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "1. 打开资源管理器\n2. 选中任意文件夹\n3. 按下 Alt+M\n4. 在弹出的窗口中输入备注\n5. 点击空白处自动保存",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 16),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "备注存储在 NTFS 备用数据流中，\n完全不可见，不影响文件。",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            TextWrapping = TextWrapping.Wrap,
        });

        return panel;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
