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

public class FolderNotePluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI, IPluginCommandHandler
{
    private IHostApi _host = null!;
    private IHotKeyService _hotKey = null!;
    private string? _registeredHotkey;

    public string Id => "com.long.folder-note";
    public string Name => "文件夹备注助手";
    public string Version => "1.1.0";
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
        if (result.IsSuccess)
        {
            _registeredHotkey = "Alt+M";
        }
        else
        {
            Log.Warning("[FolderNotePlugin] Alt+M 冲突，尝试 Ctrl+Alt+M");
            result = await _hotKey.RegisterAsync("Ctrl+Alt+M", OnHotkeyTriggered);
            if (result.IsSuccess)
                _registeredHotkey = "Ctrl+Alt+M";
            else
                Log.Warning("[FolderNotePlugin] 热键不可用，命令中心入口仍可执行");
        }

        State = PluginState.Running;
        Log.Information("[FolderNotePlugin] 已启动，入口 {Hotkey}", _registeredHotkey ?? "命令中心");
        return true;
    }

    public async Task<bool> StopAsync()
    {
        if (_registeredHotkey != null)
            await _hotKey.UnregisterAsync(_registeredHotkey);
        _registeredHotkey = null;

        State = PluginState.Stopped;
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

    private async Task ShowNoteHudAsync(string? requestedFolderPath = null)
    {
        var shell = _host.ShellSelection;
        var ads = _host.ADS;

        var folderPath = requestedFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            var folderResult = await shell.GetActiveExplorerFolderPathAsync();
            if (!folderResult.IsSuccess || folderResult.Data == null)
            {
                FloatingHudWindow.ShowToast("请先打开资源管理器并选中文件夹。");
                return;
            }

            folderPath = folderResult.Data;
        }

        if (!Directory.Exists(folderPath))
        {
            FloatingHudWindow.ShowToast("目标文件夹不存在。");
            return;
        }

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

    public async Task<LongBetterWindows.Host.Contracts.PluginCommandResult> ExecuteCommandAsync(
        LongBetterWindows.Host.Contracts.PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (invocation.CommandId != "folder-note.edit")
        {
            return LongBetterWindows.Host.Contracts.PluginCommandResult.Failure(
                $"未知文件夹备注命令: {invocation.CommandId}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var folderPath = invocation.InputType == LongBetterWindows.Host.Contracts.AcceptedInputType.Folder
            ? invocation.Paths.FirstOrDefault()
            : invocation.InputType == LongBetterWindows.Host.Contracts.AcceptedInputType.ExplorerSelection
                ? invocation.Paths.FirstOrDefault(Directory.Exists)
                : null;
        await ShowNoteHudAsync(folderPath);
        return LongBetterWindows.Host.Contracts.PluginCommandResult.Success(
            outputs: string.IsNullOrWhiteSpace(folderPath)
                ? null
                : new Dictionary<string, LongBetterWindows.Host.Contracts.PluginCommandOutput>
                {
                    ["selected-folder"] = new(
                        LongBetterWindows.Host.Contracts.PluginCommandOutputType.Path,
                        folderPath),
                });
    }

    public void ShowMainUI() => OnHotkeyTriggered();

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "文件夹备注助手", Id, _registeredHotkey ?? "命令中心",
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
