using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class SuperPanelSearchSessionTests
{
    [Fact]
    public async Task PresetContext_EmitsContextAndCompletedResults()
    {
        var provider = new RecordingSearchProvider();
        using var session = CreateSession(
            new ContextCaptureService(Array.Empty<IContextProvider>()),
            provider);
        var contexts = new List<SuperPanelContextUpdate>();
        var results = new List<SuperPanelResultsUpdate>();
        session.ContextUpdated += (_, update) => contexts.Add(update);
        session.ResultsUpdated += (_, update) => results.Add(update);
        var context = Snapshot("preset");

        await session.StartWithContextAsync(context);

        Assert.Same(context, session.CurrentContext);
        Assert.Single(contexts);
        Assert.False(contexts[0].IsLoading);
        Assert.Contains(results, update => !update.Completed);
        var completed = Assert.Single(results.Where(update => update.Completed));
        Assert.Equal("result.preset", Assert.Single(completed.Results).Id);
    }

    [Fact]
    public async Task NewCapture_SupersedesSlowerPreviousCapture()
    {
        var capture = new ContextCaptureService(
            [new DelayedContextProvider()],
            TimeSpan.FromSeconds(2));
        using var session = CreateSession(capture, new RecordingSearchProvider());
        var capturedIds = new List<string>();
        session.ContextUpdated += (_, update) =>
        {
            if (!update.IsLoading && update.Snapshot.Items.Count > 0)
                capturedIds.Add(update.Snapshot.Items[0].Id);
        };

        var first = session.StartCaptureAsync(
            new ContextCaptureRequest((IntPtr)1, DateTimeOffset.UtcNow));
        await Task.Delay(20);
        var second = session.StartCaptureAsync(
            new ContextCaptureRequest((IntPtr)2, DateTimeOffset.UtcNow));
        await Task.WhenAll(first, second);

        Assert.Equal(["context.2"], capturedIds);
        Assert.Equal("context.2", Assert.Single(session.CurrentContext.Items).Id);
    }

    [Fact]
    public async Task RefreshSearch_ReusesContextAndIncludesCustomGroupPreferences()
    {
        var storage = new MemoryStorage();
        var groups = new SuperPanelGroupService(storage);
        var custom = await groups.CreateAsync("Work");
        Assert.NotNull(custom);
        await groups.AddResultAsync(custom!.Id, "preferred.result");
        var provider = new RecordingSearchProvider();
        using var session = new SuperPanelSearchSession(
            new ContextCaptureService(Array.Empty<IContextProvider>()),
            new SearchCoordinator([provider]),
            groups);
        var context = Snapshot("stable");
        await session.StartWithContextAsync(context);
        provider.Requests.Clear();

        await session.RefreshSearchAsync();

        var request = Assert.Single(provider.Requests);
        Assert.Same(context, request.Context);
        Assert.Contains("preferred.result", request.AdditionalPreferredResultIds!);
    }

    private static SuperPanelSearchSession CreateSession(
        ContextCaptureService capture,
        ISearchProvider provider)
        => new(
            capture,
            new SearchCoordinator([provider]),
            new SuperPanelGroupService(new MemoryStorage()));

    private static ContextSnapshot Snapshot(string id)
        => new(DateTimeOffset.UtcNow,
        [
            new ContextItem
            {
                Id = id,
                Source = ContextSource.Clipboard,
                Label = id,
                Text = id,
                CompatibleInputTypes = [AcceptedInputType.Text],
            },
        ]);

    private sealed class RecordingSearchProvider : ISearchProvider
    {
        public string Id => "recording";
        public int Priority => 1;
        public List<SearchRequest> Requests { get; } = new();

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var contextId = request.Context.Items.FirstOrDefault()?.Id ?? "empty";
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(
            [
                new SearchResultItem
                {
                    Id = $"result.{contextId}",
                    ProviderId = Id,
                    Title = contextId,
                    PrimaryAction = new SearchResultAction(
                        SearchActionKind.ContinueSearch, contextId),
                },
            ]);
        }
    }

    private sealed class DelayedContextProvider : IContextProvider
    {
        public string Id => "delayed";
        public int Priority => 1;

        public async Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.ForegroundWindowHandle == (IntPtr)1)
                await Task.Delay(250, cancellationToken);
            return Snapshot($"context.{request.ForegroundWindowHandle}").Items;
        }
    }

    private sealed class MemoryStorage : IStorageService
    {
        private readonly Dictionary<string, string> _values = new();

        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(
                _values.TryGetValue(key, out var value) ? value : null));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            _values.Remove(key);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(_values.ContainsKey(key)));
    }
}
