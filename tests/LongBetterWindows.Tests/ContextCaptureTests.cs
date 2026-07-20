using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class ContextCaptureTests
{
    [Fact]
    public void Snapshot_SelectBestUsesCompatibleTypeAndNeverFallsBackToNone()
    {
        var item = new ContextItem
        {
            Id = "file",
            Source = ContextSource.ExplorerSelection,
            Label = "File",
            Paths = new[] { "C:\\demo.txt" },
            CompatibleInputTypes = new[] { AcceptedInputType.File, AcceptedInputType.ExplorerSelection },
        };
        var snapshot = new ContextSnapshot(DateTimeOffset.UtcNow, new[] { item });

        var compatible = snapshot.SelectBest(new[] { AcceptedInputType.ExplorerSelection });
        var incompatible = snapshot.SelectBest(new[] { AcceptedInputType.None });

        Assert.Equal(AcceptedInputType.ExplorerSelection, compatible!.InputType);
        Assert.Null(incompatible);
    }

    [Fact]
    public async Task ClipboardProvider_ClassifiesUrlWithoutLosingTextCompatibility()
    {
        var provider = new ClipboardContextProvider(
            new FakeClipboardService("https://example.test/page"));

        var result = Assert.Single(await provider.CaptureAsync(
            new ContextCaptureRequest(IntPtr.Zero, DateTimeOffset.UtcNow)));

        Assert.Equal(ContextSource.Clipboard, result.Source);
        Assert.Equal(AcceptedInputType.Url, result.CompatibleInputTypes[0]);
        Assert.Contains(AcceptedInputType.Clipboard, result.CompatibleInputTypes);
        Assert.Contains(AcceptedInputType.Text, result.CompatibleInputTypes);
        Assert.DoesNotContain("page", result.Id);
    }

    [Fact]
    public async Task ClipboardProvider_EncodesImageAsMemoryOnlyPngContext()
    {
        var pixels = new byte[] { 0x20, 0x40, 0x60, 0xff };
        var image = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        image.Freeze();
        var provider = new ClipboardImageContextProvider(
            new FakeClipboardService(string.Empty, image));

        var result = Assert.Single(await provider.CaptureAsync(
            new ContextCaptureRequest(IntPtr.Zero, DateTimeOffset.UtcNow)));

        Assert.Equal("clipboard.image", result.Id);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, result.ImagePng![..4]);
        Assert.Equal(new[] { AcceptedInputType.Image }, result.CompatibleInputTypes);
        Assert.Equal(ContextSensitivity.Sensitive, result.Sensitivity);
    }

    [Fact]
    public async Task CaptureService_TimesOutSlowProviderWithoutBlockingFastResult()
    {
        var fast = new FakeProvider("fast", 10, new ContextItem
        {
            Id = "fast.item",
            Source = ContextSource.Clipboard,
            Label = "Fast",
            Text = "value",
            CompatibleInputTypes = new[] { AcceptedInputType.Text },
        });
        var service = new ContextCaptureService(
            new IContextProvider[] { new SlowProvider(), fast },
            TimeSpan.FromMilliseconds(80));
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await service.CaptureAsync(
            new ContextCaptureRequest(IntPtr.Zero, DateTimeOffset.UtcNow));

        stopwatch.Stop();
        Assert.Single(snapshot.Items);
        Assert.Equal("fast.item", snapshot.Items[0].Id);
        Assert.True(stopwatch.ElapsedMilliseconds < 500);
    }

    [Fact]
    public async Task CaptureService_TimesOutProviderThatBlocksBeforeReturningTask()
    {
        var fast = new FakeProvider("fast", 10, Item("fast"));
        var service = new ContextCaptureService(
            new IContextProvider[] { new SynchronouslyBlockingProvider(), fast },
            TimeSpan.FromMilliseconds(60));
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await service.CaptureAsync(
            new ContextCaptureRequest(IntPtr.Zero, DateTimeOffset.UtcNow));

        stopwatch.Stop();
        Assert.Single(snapshot.Items);
        Assert.True(stopwatch.ElapsedMilliseconds < 250,
            $"Synchronous provider blocked capture for {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public void CommandRegistry_MultipleContextTypesRecommendEveryCompatibleCommand()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(new PluginManifest
        {
            Id = "context",
            Name = "Context",
            Version = "1.0.0",
            EntryPoint = "context.dll",
            Commands = new List<PluginCommand>
            {
                new()
                {
                    Id = "url",
                    Title = "Open URL",
                    AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Url },
                },
                new()
                {
                    Id = "file",
                    Title = "Open File",
                    AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.File },
                },
            },
        });

        var results = registry.SearchWithInputs(
            string.Empty,
            new[] { AcceptedInputType.Url, AcceptedInputType.File });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Snapshot_WithoutRemovesOnlyRequestedContext()
    {
        var items = new[]
        {
            Item("one"),
            Item("two"),
        };
        var snapshot = new ContextSnapshot(DateTimeOffset.UtcNow, items);

        var updated = snapshot.Without("ONE");

        var remaining = Assert.Single(updated.Items);
        Assert.Equal("two", remaining.Id);
    }

    private static ContextItem Item(string id) => new()
    {
        Id = id,
        Source = ContextSource.Clipboard,
        Label = id,
        Text = id,
        CompatibleInputTypes = new[] { AcceptedInputType.Text },
    };

    private sealed class FakeClipboardService : IClipboardService
    {
        private readonly string _text;
        private readonly BitmapSource? _image;
        public event EventHandler<ClipboardChangedEventArgs>? ClipboardChanged
        {
            add { }
            remove { }
        }
        public bool IsMonitoring { get; private set; }

        public FakeClipboardService(string text, BitmapSource? image = null)
            => (_text, _image) = (text, image);
        public Task<HostApiResponse<string?>> GetTextAsync()
            => Task.FromResult(HostApiResponse<string?>.Success(_text));

        public Task<HostApiResponse<BitmapSource?>> GetImageAsync()
            => Task.FromResult(HostApiResponse<BitmapSource?>.Success(_image));
        public Task<HostApiResponse> SetTextAsync(string text)
            => Task.FromResult(HostApiResponse.Success());
        public Task<HostApiResponse> SetImageAsync(BitmapSource image)
            => Task.FromResult(HostApiResponse.Success());
        public Task<HostApiResponse> ClearAsync()
            => Task.FromResult(HostApiResponse.Success());
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

    private sealed class FakeProvider : IContextProvider
    {
        private readonly ContextItem _item;
        public FakeProvider(string id, int priority, ContextItem item)
            => (Id, Priority, _item) = (id, priority, item);
        public string Id { get; }
        public int Priority { get; }
        public Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContextItem>>(new[] { _item });
    }

    private sealed class SlowProvider : IContextProvider
    {
        public string Id => "slow";
        public int Priority => 100;
        public async Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<ContextItem>();
        }
    }

    private sealed class SynchronouslyBlockingProvider : IContextProvider
    {
        public string Id => "blocking";
        public int Priority => 200;

        public Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            Thread.Sleep(500);
            return Task.FromResult<IReadOnlyList<ContextItem>>(Array.Empty<ContextItem>());
        }
    }
}
