using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    internal sealed class ClipboardMonitoringLeaseManager : IDisposable
    {
        private readonly Func<Task<HostApiResponse>> _start;
        private readonly Func<Task<HostApiResponse>> _stop;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _leaseCount;
        private bool _disposed;

        internal ClipboardMonitoringLeaseManager(
            Func<Task<HostApiResponse>> start,
            Func<Task<HostApiResponse>> stop)
        {
            _start = start;
            _stop = stop;
        }

        internal bool IsActive => Volatile.Read(ref _leaseCount) > 0;
        internal int LeaseCount => Volatile.Read(ref _leaseCount);

        internal async Task<HostApiResponse> AcquireAsync()
        {
            await _gate.WaitAsync();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_leaseCount > 0)
                {
                    _leaseCount++;
                    return HostApiResponse.Success();
                }

                var result = await _start();
                if (result.IsSuccess)
                    _leaseCount = 1;
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task<HostApiResponse> ReleaseAsync()
        {
            await _gate.WaitAsync();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_leaseCount == 0)
                    return HostApiResponse.Success();
                if (_leaseCount > 1)
                {
                    _leaseCount--;
                    return HostApiResponse.Success();
                }

                var result = await _stop();
                if (result.IsSuccess)
                    _leaseCount = 0;
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task<HostApiResponse> StopAllAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_leaseCount == 0)
                    return HostApiResponse.Success();

                var result = await _stop();
                if (result.IsSuccess)
                    _leaseCount = 0;
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _gate.Dispose();
        }
    }
}
