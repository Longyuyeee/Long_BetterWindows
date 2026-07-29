using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.PluginSdk.Wpf;
using Serilog;

namespace FolderNotePlugin;

public class FolderNotePluginImpl :
    ILongPlugin,
    IHasSettingsUI,
    IHasMainUI,
    IPluginCommandHandler,
    IPluginLanguageLifecycle
{
    private IHostApi _host = null!;
    private IHotKeyService _hotKey = null!;
    private IPluginSettingsService _pluginSettings = null!;
    private INotificationService _notification = null!;
    private string _configuredHotkey = "Alt+M";
    private string? _registeredHotkey;
    private AnchoredTextEditorWindow? _activeHud;
    private readonly List<WeakReference<HotkeySettingsControl>> _settings = [];
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.folder-note";
    public string Name => Text("plugin.name", "文件夹备注助手");
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public async Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        _hotKey = host.HotKey; // 若无 system.hotkey 能力会抛出 UnauthorizedAccessException
        _pluginSettings = host.Settings;
        _notification = host.Notification;
        var configured = await _pluginSettings.GetAsync("hotkey");
        if (configured.IsSuccess && !string.IsNullOrWhiteSpace(configured.Data))
            _configuredHotkey = configured.Data;

        Log.Information("[FolderNotePlugin] 初始化完成");
        return true;
    }

    public async Task<bool> StartAsync()
    {
        var result = await _hotKey.RegisterAsync(
            _configuredHotkey,
            OnHotkeyTriggered);
        if (result.IsSuccess)
        {
            _registeredHotkey = _configuredHotkey;
        }
        else
        {
            Log.Warning(
                "[FolderNotePlugin] {Hotkey} 冲突，尝试 Ctrl+Alt+M",
                _configuredHotkey);
            if (!_configuredHotkey.Equals(
                    "Ctrl+Alt+M",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = await _hotKey.RegisterAsync(
                    "Ctrl+Alt+M",
                    OnHotkeyTriggered);
                if (result.IsSuccess)
                    _registeredHotkey = "Ctrl+Alt+M";
                else
                    Log.Warning("[FolderNotePlugin] 热键不可用，命令中心入口仍可执行");
            }
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
        Application.Current.Dispatcher.Invoke(() => _activeHud?.Close());
        _activeHud = null;

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

    private async Task<PluginCommandResult> ShowNoteHudAsync(
        string? requestedFolderPath = null)
    {
        var shell = _host.ShellSelection;
        var ads = _host.ADS;

        var folderPath = requestedFolderPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            var folderResult = await shell.GetActiveExplorerFolderPathAsync();
            if (!folderResult.IsSuccess || folderResult.Data == null)
            {
                _ = _notification.ShowAsync(Name, Text(
                    "error.selectFolder",
                    "请先打开资源管理器并选中文件夹。"));
                return PluginCommandResult.Failure(Text(
                    "error.selectFolder",
                    "请先打开资源管理器并选中文件夹。"));
            }

            folderPath = folderResult.Data;
        }

        if (!Directory.Exists(folderPath))
        {
            _ = _notification.ShowAsync(Name, Text(
                "error.folderMissing",
                "目标文件夹不存在。"));
            return PluginCommandResult.Failure(Text(
                "error.folderMissing",
                "目标文件夹不存在。"));
        }

        var noteResult = await ads.ReadAsync(folderPath, "long_note");

        string? existingNote = null;
        if (noteResult.IsSuccess && noteResult.Data != null)
        {
            existingNote = noteResult.Data;
        }
        else if (noteResult.ErrorCode != ApiErrorCode.StreamNotFound)
        {
            Log.Warning(
                "[FolderNotePlugin] 备注读取失败: {Path}, {Error}",
                folderPath,
                noteResult.ErrorMessage);
            var message = Text(
                "error.loadFailed",
                "文件夹备注读取失败，请检查权限后重试。");
            _ = _notification.ShowAsync(Name, message);
            return PluginCommandResult.Failure(message);
        }

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
            var window = AnchoredTextEditorWindow.ShowAt(
                hudX,
                hudY,
                existingNote,
                Path.GetFileName(folderPath),
                async (text) =>
                {
                    var result = string.IsNullOrEmpty(text)
                        ? await ads.DeleteAsync(folderPath, "long_note")
                        : await ads.WriteAsync(folderPath, "long_note", text);
                    if (!result.IsSuccess)
                    {
                        Log.Warning(
                            "[FolderNotePlugin] 备注保存失败: {Path}, {Error}",
                            folderPath,
                            result.ErrorMessage);
                        throw new InvalidOperationException(Text(
                            "error.saveFailed",
                            "文件夹备注保存失败，请重试。"));
                    }

                    Log.Information("[FolderNotePlugin] 备注已保存: {Path}", folderPath);
                },
                CreateHudLocalization());
            _activeHud = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeHud, window))
                    _activeHud = null;
            };
        });
        return PluginCommandResult.Success(
            outputs: new Dictionary<string, PluginCommandOutput>
            {
                ["selected-folder"] = new(
                    PluginCommandOutputType.Path,
                    folderPath),
            });
    }

    public async Task<LongBetterWindows.Host.Contracts.PluginCommandResult> ExecuteCommandAsync(
        LongBetterWindows.Host.Contracts.PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        if (invocation.CommandId != "folder-note.edit")
        {
            return LongBetterWindows.Host.Contracts.PluginCommandResult.Failure(
                string.Format(
                    Text(
                        "error.unknownCommand",
                        "未知文件夹备注命令: {0}"),
                    invocation.CommandId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var folderPath = invocation.InputType == LongBetterWindows.Host.Contracts.AcceptedInputType.Folder
            ? invocation.Paths.FirstOrDefault()
            : invocation.InputType == LongBetterWindows.Host.Contracts.AcceptedInputType.ExplorerSelection
                ? invocation.Paths.FirstOrDefault(Directory.Exists)
                : null;
        return await ShowNoteHudAsync(folderPath);
    }

    public void ShowMainUI() => OnHotkeyTriggered();

    public FrameworkElement CreateSettingsUI()
    {
        var control = new HotkeySettingsControl(
            _hotKey,
            Name,
            Id,
            _registeredHotkey ?? Text("settings.commandCenter", "命令中心"),
            async newHotkey =>
            {
                var result = await _pluginSettings.SetAsync(
                    "hotkey",
                    newHotkey);
                if (!result.IsSuccess)
                    return result;
                _configuredHotkey = newHotkey;
                _registeredHotkey = newHotkey;
                return HostApiResponse.Success();
            },
            CreateSettingsLocalization(),
            OnHotkeyTriggered);
        _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
        _settings.Add(new WeakReference<HotkeySettingsControl>(control));
        return control;
    }

    public Task OnLanguageChangedAsync(
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _strings = context.Resources;
        var application = Application.Current;
        if (application is null)
            return Task.CompletedTask;

        application.Dispatcher.Invoke(() =>
        {
            _activeHud?.ApplyLocalization(CreateHudLocalization());
            _settings.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in _settings)
            {
                if (reference.TryGetTarget(out var control))
                    control.ApplyLocalization(Name, CreateSettingsLocalization());
            }
        });
        return Task.CompletedTask;
    }

    private AnchoredTextEditorLocalization CreateHudLocalization()
        => new(
            Text("hud.title", "备注"),
            Text("hud.inputAutomationName", "文件夹备注内容"),
            Text("hud.emptyHint", "输入备注内容..."),
            Text("hud.modifiedHint", "已修改 · Ctrl+Enter 保存"));

    private HotkeySettingsLocalization CreateSettingsLocalization()
        => new(
            Text("settings.currentHotkey", "当前快捷键"),
            Text("settings.apply", "应用"),
            Text("settings.unchanged", "未修改"),
            Text("settings.conflict", "冲突: 已被「{0}」占用"),
            Text("settings.updated", "已更新"),
            Text("settings.changeFailed", "修改失败: {0}"),
            Text(
                "settings.formatHint",
                "格式: Ctrl+K  Alt+M  Win+N  Ctrl+Shift+Space  F6"));

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
