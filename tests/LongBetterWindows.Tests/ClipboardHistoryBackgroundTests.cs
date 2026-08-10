using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipboardHistoryBackground;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;

namespace LongBetterWindows.Tests;

public sealed class ClipboardHistoryBackgroundTests
{
    [Fact]
    public async Task OversizedClipboardText_IsNotPersisted()
    {
        var storage = new MemoryStorage();
        var clipboard = new ClipboardStub();
        var plugin = CreatePlugin(storage, clipboard);

        Assert.True(await plugin.StartAsync());
        clipboard.Raise(new string('x', 65537));
        Assert.True(await plugin.StopAsync());

        Assert.Null(storage.Value);
        Assert.Equal(0, storage.CompareExchangeCount);
    }

    [Fact]
    public async Task MalformedHistory_IsNotOverwrittenByClipboardEvent()
    {
        const string malformed = "{not-json";
        var storage = new MemoryStorage(malformed);
        var clipboard = new ClipboardStub();
        var plugin = CreatePlugin(storage, clipboard);

        Assert.True(await plugin.StartAsync());
        clipboard.Raise("new value");
        Assert.True(await plugin.StopAsync());

        Assert.Equal(malformed, storage.Value);
        Assert.Equal(0, storage.CompareExchangeCount);
    }

    [Fact]
    public async Task PersistedHistory_RespectsAggregateCharacterBudget()
    {
        var existing = Enumerable.Range(0, 90)
            .Select(index => new
            {
                id = index.ToString(),
                content = new string((char)('a' + index % 26), 12000),
                type = "text",
                timestamp = DateTimeOffset.UtcNow.AddMinutes(-index),
            });
        var storage = new MemoryStorage(JsonSerializer.Serialize(existing));
        var clipboard = new ClipboardStub();
        var plugin = CreatePlugin(storage, clipboard);

        Assert.True(await plugin.StartAsync());
        clipboard.Raise("fresh value");
        Assert.True(await plugin.StopAsync());

        using var document = JsonDocument.Parse(Assert.IsType<string>(storage.Value));
        var items = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal("fresh value", items[0].GetProperty("content").GetString());
        Assert.True(items.Length <= 500);
        Assert.True(items.Sum(item =>
            item.GetProperty("content").GetString()!.Length) <= 1_000_000);
        Assert.Equal(1, storage.CompareExchangeCount);
    }

    private static ClipboardHistoryBackgroundPlugin CreatePlugin(
        MemoryStorage storage,
        ClipboardStub clipboard)
    {
        var host = new PluginTestHost()
            .Grant<IClipboardService>(clipboard, "system.clipboard.monitor")
            .Grant<IStorageService>(storage)
            .Grant<IHotKeyService>(new HotKeyStub());
        var plugin = new ClipboardHistoryBackgroundPlugin();
        Assert.True(plugin.InitializeAsync(host).GetAwaiter().GetResult());
        return plugin;
    }

    private sealed class ClipboardStub : IClipboardService
    {
        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged;
        public bool IsMonitoring { get; private set; }

        public void Raise(string text) => ClipboardChanged?.Invoke(
            this,
            new ClipboardChangedEventArgs
            {
                ContentType = ClipboardContentType.Text,
                Text = text,
                Timestamp = DateTime.Now,
            });

        public Task<HostApiResponse<string?>> GetTextAsync() =>
            Task.FromResult(HostApiResponse<string?>.Success(null));

        public Task<HostApiResponse<BitmapSource?>> GetImageAsync() =>
            Task.FromResult(HostApiResponse<BitmapSource?>.Success(null));

        public Task<HostApiResponse> SetTextAsync(string text) =>
            Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> SetImageAsync(BitmapSource image) =>
            Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> ClearAsync() =>
            Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> StartMonitoringAsync()
        {
            IsMonitoring = true;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> StopMonitoringAsync()
        {
            IsMonitoring = false;
            return Task.FromResult(HostApiResponse.Success());
        }
    }

    private sealed class MemoryStorage : IStorageService
    {
        public MemoryStorage(string? value = null) => Value = value;

        public string? Value { get; private set; }
        public int CompareExchangeCount { get; private set; }

        public Task<HostApiResponse<string?>> GetAsync(string key) =>
            Task.FromResult(HostApiResponse<string?>.Success(Value));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            Value = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> CompareExchangeAsync(
            string key,
            string? expectedValue,
            string value)
        {
            CompareExchangeCount++;
            if (!string.Equals(Value, expectedValue, StringComparison.Ordinal))
                return Task.FromResult(HostApiResponse<bool>.Success(false));
            Value = value;
            return Task.FromResult(HostApiResponse<bool>.Success(true));
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            Value = null;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key) =>
            Task.FromResult(HostApiResponse<bool>.Success(Value is not null));
    }

    private sealed class HotKeyStub : IHotKeyService
    {
        public Task<HostApiResponse> RegisterAsync(string hotkey, Action callback) =>
            Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> RegisterAsync(
            string hotkey,
            string pluginId,
            Action callback) => Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse> UnregisterAsync(string hotkey) =>
            Task.FromResult(HostApiResponse.Success());

        public Task<HostApiResponse<bool>> IsConflictAsync(string hotkey) =>
            Task.FromResult(HostApiResponse<bool>.Success(false));

        public Task<HostApiResponse<bool>> IsConflictAsync(
            string hotkey,
            string? excludedHotkey) =>
            Task.FromResult(HostApiResponse<bool>.Success(false));

        public string? GetOwner(string hotkey) => null;

        public IReadOnlyDictionary<string, string> GetAllHotkeys() =>
            new Dictionary<string, string>();

        public Task<HostApiResponse> ChangeHotkeyAsync(
            string oldHotkey,
            string newHotkey,
            string pluginId,
            Action callback) => Task.FromResult(HostApiResponse.Success());
    }
}
