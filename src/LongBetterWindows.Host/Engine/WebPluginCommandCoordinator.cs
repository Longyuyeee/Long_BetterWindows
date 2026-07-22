using System.Collections.Concurrent;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WebPluginCommandCoordinator : IDisposable
    {
        private readonly string _pluginId;
        private readonly Func<string, Task> _sendMessage;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<PluginCommandResult>>
            _pending = new(StringComparer.Ordinal);

        internal WebPluginCommandCoordinator(
            string pluginId,
            Func<string, Task> sendMessage,
            TimeSpan? timeout = null)
        {
            _pluginId = pluginId;
            _sendMessage = sendMessage;
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        internal async Task<PluginCommandResult> SendAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            if (_pending.Count >= 64)
                return PluginCommandResult.Failure("Web plugin already has 64 pending commands.");
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<PluginCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(requestId, completion))
                return PluginCommandResult.Failure("Web command request could not be registered.");

            using var timeout = new CancellationTokenSource(_timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            using var registration = linked.Token.Register(() => completion.TrySetCanceled(linked.Token));
            try
            {
                await _sendMessage(WebPluginBridgeProtocol.SerializeCommand(requestId, invocation));
                return await completion.Task;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return PluginCommandResult.Failure(
                    $"Web command timed out after {_timeout.TotalSeconds:N0} seconds.");
            }
            finally
            {
                _pending.TryRemove(requestId, out _);
            }
        }

        internal bool TryHandle(string json)
        {
            var message = WebPluginBridgeProtocol.ParseCommandResult(json);
            if (message is null) return false;
            if (!_pending.TryGetValue(message.RequestId, out var completion))
            {
                Log.Debug("[Web:{Id}] Ignored stale command result: {RequestId}",
                    _pluginId,
                    message.RequestId);
                return true;
            }
            if (!WebPluginBridgeProtocol.TryCreateCommandResult(
                message,
                out var result,
                out var error))
            {
                Log.Warning("[Web:{Id}] Rejected command result: {Error}", _pluginId, error);
            }
            completion.TrySetResult(result);
            return true;
        }

        public void Dispose()
        {
            foreach (var pending in _pending.Values) pending.TrySetCanceled();
            _pending.Clear();
        }
    }
}
