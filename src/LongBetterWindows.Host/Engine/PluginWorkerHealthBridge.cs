using LongBetterWindows.PluginIpc.Client;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Engine;

public static class PluginWorkerHealthBridge
{
    public static void RecordCommandOutcome(
        PluginRuntimeHealthMonitor health,
        string pluginId,
        TimeSpan duration,
        Exception? error)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (error is null)
        {
            health.RecordSuccess(pluginId, duration);
            return;
        }

        if (error is IpcRemoteException { Code: IpcErrorCodes.Cancelled })
        {
            health.RecordCancellation(pluginId, duration);
            return;
        }
        if (error is IpcRemoteException { Code: IpcErrorCodes.Timeout })
        {
            health.RecordFailure(
                pluginId,
                duration,
                PluginRuntimeFailureKind.WorkerTimeout);
            return;
        }
        if (error is PluginWorkerExitedException)
        {
            health.RecordException(
                pluginId,
                duration,
                PluginRuntimeFailureKind.WorkerCrashed);
            return;
        }

        health.RecordException(pluginId, duration);
    }

    public static void RecordLifecycleState(
        PluginRuntimeHealthMonitor health,
        string pluginId,
        string workerState)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var state = workerState switch
        {
            "initialized" => PluginRuntimeLifecycleState.Loaded,
            "running" => PluginRuntimeLifecycleState.Running,
            "background" => PluginRuntimeLifecycleState.Background,
            "stopped" or "released" => PluginRuntimeLifecycleState.Stopped,
            _ => throw new ArgumentOutOfRangeException(nameof(workerState)),
        };
        health.RecordLifecycleTransition(pluginId, state);
    }
}
