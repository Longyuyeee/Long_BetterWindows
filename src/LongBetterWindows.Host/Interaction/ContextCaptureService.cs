using Serilog;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class ContextCaptureService
    {
        private readonly IReadOnlyList<IContextProvider> _providers;
        private readonly TimeSpan _providerTimeout;

        public ContextCaptureService(
            IEnumerable<IContextProvider> providers,
            TimeSpan? providerTimeout = null)
        {
            _providers = providers.OrderByDescending(provider => provider.Priority).ToList();
            _providerTimeout = providerTimeout ?? TimeSpan.FromMilliseconds(450);
        }

        public async Task<ContextSnapshot> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            var captures = _providers.Select(provider =>
                CaptureProviderAsync(provider, request, cancellationToken));
            var results = await Task.WhenAll(captures);
            return new ContextSnapshot(request.CapturedAt, results.SelectMany(items => items));
        }

        private async Task<IReadOnlyList<ContextItem>> CaptureProviderAsync(
            IContextProvider provider,
            ContextCaptureRequest request,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_providerTimeout);
            try
            {
                var capture = Task.Factory.StartNew(
                    () => provider.CaptureAsync(request, timeout.Token),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
                return await capture
                    .WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Debug("上下文 Provider 超时: {ProviderId}", provider.Id);
            }
            catch (OperationCanceledException)
            {
                // The whole capture was superseded by a newer palette invocation.
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "上下文 Provider 失败: {ProviderId}", provider.Id);
            }

            return Array.Empty<ContextItem>();
        }
    }
}
