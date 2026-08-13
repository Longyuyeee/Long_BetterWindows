using System.Windows.Threading;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class DispatcherTaskWaiterTests
{
    [Fact]
    public void Wait_PumpsQueuedDispatcherWorkUntilTaskCompletes()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.InvokeAsync(
            () => 42,
            DispatcherPriority.Background);

        var result = DispatcherTaskWaiter.Wait(operation.Task, dispatcher);

        Assert.Equal(42, result);
    }

    [Fact]
    public void Wait_PropagatesQueuedDispatcherFailure()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.InvokeAsync<int>(
            () => throw new InvalidOperationException("synthetic failure"),
            DispatcherPriority.Background);

        var exception = Assert.Throws<InvalidOperationException>(
            () => DispatcherTaskWaiter.Wait(operation.Task, dispatcher));

        Assert.Equal("synthetic failure", exception.Message);
    }
}
