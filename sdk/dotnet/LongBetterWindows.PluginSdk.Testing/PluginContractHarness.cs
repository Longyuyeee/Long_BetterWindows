using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.PluginSdk.Testing;

public static class PluginContractHarness
{
    public static void VerifyIdentity(
        ILongPlugin plugin,
        PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(manifest);

        if (!string.Equals(plugin.Id, manifest.Id, StringComparison.Ordinal))
            throw new PluginContractException(
                $"Plugin ID '{plugin.Id}' does not match manifest ID '{manifest.Id}'.");
        if (!string.Equals(plugin.Version, manifest.Version, StringComparison.Ordinal))
            throw new PluginContractException(
                $"Plugin version '{plugin.Version}' does not match manifest version '{manifest.Version}'.");
        if (string.IsNullOrWhiteSpace(plugin.Name))
            throw new PluginContractException("Plugin name must not be empty.");
    }

    public static async Task<PluginLifecycleResult> VerifyLifecycleAsync(
        ILongPlugin plugin,
        IHostApi host)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(host);

        var initialState = plugin.State;
        if (initialState != PluginState.Loaded)
            throw new PluginContractException(
                $"Expected initial state Loaded, observed {initialState}.");
        if (!await plugin.InitializeAsync(host))
            throw new PluginContractException("InitializeAsync returned false.");
        if (!await plugin.StartAsync())
            throw new PluginContractException("StartAsync returned false.");
        if (plugin.State != PluginState.Running)
            throw new PluginContractException(
                $"Expected Running after StartAsync, observed {plugin.State}.");
        if (!await plugin.StopAsync())
            throw new PluginContractException("StopAsync returned false.");
        if (plugin.State != PluginState.Stopped)
            throw new PluginContractException(
                $"Expected Stopped after StopAsync, observed {plugin.State}.");

        return new PluginLifecycleResult(
            initialState,
            plugin.State,
            Initialized: true,
            Started: true,
            Stopped: true);
    }

    public static async Task<PluginCommandResult> ExecuteCommandAsync(
        ILongPlugin plugin,
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(invocation);

        if (plugin is not IPluginCommandHandler handler)
            throw new PluginContractException(
                $"{plugin.GetType().Name} does not implement {nameof(IPluginCommandHandler)}.");
        return await handler.ExecuteCommandAsync(invocation, cancellationToken);
    }

    public static async Task ApplyLanguageAsync(
        ILongPlugin plugin,
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(context);

        if (plugin is not IPluginLanguageLifecycle lifecycle)
            throw new PluginContractException(
                $"{plugin.GetType().Name} does not implement {nameof(IPluginLanguageLifecycle)}.");
        await lifecycle.OnLanguageChangedAsync(context, cancellationToken);
    }

    public static async Task VerifyResourceReleaseIsRepeatableAsync(
        ILongPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (plugin is not IPluginResourceLifecycle lifecycle)
            throw new PluginContractException(
                $"{plugin.GetType().Name} does not implement {nameof(IPluginResourceLifecycle)}.");

        await lifecycle.ReleaseResourcesAsync();
        await lifecycle.ReleaseResourcesAsync();
    }

    public static void VerifyCapabilityDenied<TService>(
        PluginTestHost host,
        Func<IHostApi, TService> access,
        string expectedCapability)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCapability);

        try
        {
            _ = access(host);
        }
        catch (UnauthorizedAccessException)
        {
            if (host.LastAccessError?.Contains(
                    $"'{expectedCapability}'",
                    StringComparison.Ordinal) == true)
            {
                return;
            }

            throw new PluginContractException(
                $"Denied access did not report capability '{expectedCapability}'.");
        }

        throw new PluginContractException(
            $"Expected capability '{expectedCapability}' to be denied.");
    }
}

public sealed record PluginLifecycleResult(
    PluginState InitialState,
    PluginState FinalState,
    bool Initialized,
    bool Started,
    bool Stopped);

public sealed class PluginContractException : Exception
{
    public PluginContractException(string message)
        : base(message)
    {
    }
}
