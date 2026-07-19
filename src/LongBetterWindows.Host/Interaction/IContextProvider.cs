namespace LongBetterWindows.Host.Interaction
{
    public interface IContextProvider
    {
        string Id { get; }
        int Priority { get; }

        Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default);
    }
}
