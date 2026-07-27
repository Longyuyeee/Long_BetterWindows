using System.Text.Json;
using System.Text.Json.Serialization;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace ClipboardHistoryBackground;

public sealed class ClipboardHistoryBackgroundPlugin :
    ILongPlugin,
    IPluginOpenRequestSource
{
    private const string StorageKey = "clipboard_history";
    private const string Hotkey = "Ctrl+Shift+V";
    private const int MaxItems = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _queueLock = new();
    private Task _mutationQueue = Task.CompletedTask;
    private IClipboardService _clipboard = null!;
    private IStorageService _storage = null!;
    private IHotKeyService _hotKey = null!;
    private bool _monitoring;
    private bool _hotkeyRegistered;

    public string Id => "com.long.clipboardhistory";
    public string Name => "Clipboard history background";
    public string Version => "1.1.0";
    public PluginState State { get; private set; } = PluginState.Loaded;
    public event Action? OpenRequested;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _clipboard = host.Clipboard;
        _storage = host.Storage;
        _hotKey = host.HotKey;
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        if (State == PluginState.Running)
            return true;

        _clipboard.ClipboardChanged += OnClipboardChanged;
        var monitor = await _clipboard.StartMonitoringAsync();
        if (!monitor.IsSuccess)
        {
            _clipboard.ClipboardChanged -= OnClipboardChanged;
            Log.Warning(
                "[ClipboardHistory.Background] 剪贴板监听启动失败: {Error}",
                monitor.ErrorMessage);
            return false;
        }
        _monitoring = true;

        var hotkey = await _hotKey.RegisterAsync(Hotkey, Id, RequestOpen);
        _hotkeyRegistered = hotkey.IsSuccess;
        if (!hotkey.IsSuccess)
        {
            Log.Warning(
                "[ClipboardHistory.Background] 热键 {Hotkey} 不可用: {Error}",
                Hotkey,
                hotkey.ErrorMessage);
        }

        State = PluginState.Running;
        Log.Information(
            "[ClipboardHistory.Background] 后台监听已启动，Hotkey={HotkeyAvailable}",
            _hotkeyRegistered);
        return true;
    }

    public async Task<bool> StopAsync()
    {
        if (_hotkeyRegistered)
            await _hotKey.UnregisterAsync(Hotkey);
        _hotkeyRegistered = false;

        if (_monitoring)
        {
            _clipboard.ClipboardChanged -= OnClipboardChanged;
            await DrainMutationsAsync();
            var result = await _clipboard.StopMonitoringAsync();
            if (!result.IsSuccess)
            {
                Log.Warning(
                    "[ClipboardHistory.Background] 剪贴板监听释放失败: {Error}",
                    result.ErrorMessage);
                return false;
            }
        }
        _monitoring = false;
        State = PluginState.Stopped;
        return true;
    }

    private void RequestOpen() => OpenRequested?.Invoke();

    private void OnClipboardChanged(
        object? sender,
        ClipboardChangedEventArgs args)
    {
        if (args.ContentType != ClipboardContentType.Text
            || string.IsNullOrWhiteSpace(args.Text))
        {
            return;
        }

        lock (_queueLock)
        {
            _mutationQueue = _mutationQueue
                .ContinueWith(
                    _ => PersistTextSafelyAsync(args.Text),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task PersistTextSafelyAsync(string text)
    {
        try
        {
            var items = await LoadHistoryAsync();
            if (items.FirstOrDefault()?.Content == text)
                return;

            items.RemoveAll(item => item.Content == text);
            items.Insert(0, new HistoryItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Content = text,
                Type = "text",
                Timestamp = DateTimeOffset.UtcNow,
            });
            if (items.Count > MaxItems)
                items.RemoveRange(MaxItems, items.Count - MaxItems);

            var result = await _storage.SetAsync(
                StorageKey,
                JsonSerializer.Serialize(items, JsonOptions));
            if (!result.IsSuccess)
            {
                Log.Warning(
                    "[ClipboardHistory.Background] 历史持久化失败: {Error}",
                    result.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "[ClipboardHistory.Background] 剪贴板事件处理失败");
        }
    }

    private async Task<List<HistoryItem>> LoadHistoryAsync()
    {
        var result = await _storage.GetAsync(StorageKey);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Data))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<HistoryItem>>(
                       result.Data,
                       JsonOptions)
                   ?? [];
        }
        catch (JsonException exception)
        {
            Log.Warning(
                exception,
                "[ClipboardHistory.Background] 现有历史格式无效，将从新记录恢复");
            return [];
        }
    }

    private Task DrainMutationsAsync()
    {
        lock (_queueLock)
            return _mutationQueue;
    }

    private sealed class HistoryItem
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = "text";

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; init; }
    }
}
