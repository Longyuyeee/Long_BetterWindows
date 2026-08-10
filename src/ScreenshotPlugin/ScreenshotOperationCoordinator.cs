namespace ScreenshotPlugin;

public sealed class ScreenshotOperationCoordinator
{
    private int _active;

    public bool IsActive => Volatile.Read(ref _active) != 0;

    public ScreenshotOperationLease? TryBegin()
        => Interlocked.CompareExchange(ref _active, 1, 0) == 0
            ? new ScreenshotOperationLease(this)
            : null;

    internal void Release()
        => Volatile.Write(ref _active, 0);
}

public sealed class ScreenshotOperationLease : IDisposable
{
    private ScreenshotOperationCoordinator? _owner;

    internal ScreenshotOperationLease(ScreenshotOperationCoordinator owner)
    {
        _owner = owner;
    }

    public void Dispose()
        => Interlocked.Exchange(ref _owner, null)?.Release();
}
