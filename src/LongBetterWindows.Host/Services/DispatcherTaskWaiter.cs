using System.Windows.Threading;

namespace LongBetterWindows.Host.Services;

internal static class DispatcherTaskWaiter
{
    internal static T Wait<T>(Task<T> task, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (task.IsCompleted || !dispatcher.CheckAccess())
            return task.GetAwaiter().GetResult();

        var frame = new DispatcherFrame();
        _ = task.ContinueWith(
            _ => dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(() => frame.Continue = false)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }
}
