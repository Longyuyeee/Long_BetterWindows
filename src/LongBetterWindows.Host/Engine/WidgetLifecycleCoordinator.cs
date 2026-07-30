using System.Globalization;

namespace LongBetterWindows.Host.Engine
{
    internal sealed class WidgetLifecycleCoordinator : IDisposable
    {
        internal static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(5);

        private readonly WebPluginBridgeContext _context;
        private readonly Action<string> _postMessage;
        private readonly TimeSpan _readyTimeout;
        private readonly object _gate = new();
        private CancellationTokenSource? _readyTimeoutCancellation;
        private long _sequence;
        private bool _mounted;
        private bool _ready;
        private bool _suspended;
        private bool _unmounted;
        private bool _visible = true;
        private WidgetSurfaceLayout _layout;

        internal WidgetLifecycleCoordinator(
            WebPluginBridgeContext context,
            Action<string> postMessage,
            TimeSpan? readyTimeout = null,
            WidgetSurfaceLayout? initialLayout = null)
        {
            _context = context;
            _postMessage = postMessage;
            _readyTimeout = readyTimeout ?? DefaultReadyTimeout;
            _layout = initialLayout ?? WidgetSurfaceLayout.Empty;
        }

        internal long Sequence
        {
            get
            {
                lock (_gate)
                    return _sequence;
            }
        }

        internal bool IsMounted
        {
            get
            {
                lock (_gate)
                    return _mounted;
            }
        }

        internal bool IsReady
        {
            get
            {
                lock (_gate)
                    return _ready;
            }
        }

        internal bool IsSuspended
        {
            get
            {
                lock (_gate)
                    return _suspended;
            }
        }

        internal void Mount()
        {
            if (!_context.IsWidget)
                return;

            if (!TryTransition(out var sequence, mounted: true))
                return;

            Post("long.widget-mounted", sequence, new
            {
                context = _context.ToHostInfo(),
                theme = "system",
                locale = CultureInfo.CurrentUICulture.Name,
                size = _layout.ToPayload(),
            });
            ScheduleReadyTimeout();
        }

        internal void Resize(WidgetSurfaceLayout layout)
        {
            long sequence;
            lock (_gate)
            {
                if (!_context.IsWidget || _unmounted || _layout == layout)
                    return;

                _layout = layout;
                if (!_mounted)
                    return;
                sequence = ++_sequence;
            }

            Post("long.widget-resized", sequence, layout.ToPayload());
        }

        internal void SetVisibility(bool visible, string reason)
        {
            long sequence;
            lock (_gate)
            {
                if (!_context.IsWidget || _unmounted || !_mounted || _visible == visible)
                    return;

                _visible = visible;
                sequence = ++_sequence;
            }

            Post("long.widget-visibility-changed", sequence, new
            {
                visible,
                reason,
            });
        }

        internal object MarkReady(object? contentVersion)
        {
            lock (_gate)
            {
                if (!_context.IsWidget)
                    return new { success = false, error = "Widget API 仅在 Widget 上下文中可用" };

                if (_unmounted)
                    return new { success = false, error = "Widget instance is already unmounted." };

                _mounted = true;
                _ready = true;
                _readyTimeoutCancellation?.Cancel();
            }

            return new
            {
                success = true,
                ready = true,
                content_version = contentVersion,
            };
        }

        internal void Suspend(string reason = "hidden", int deadlineMs = 30_000)
        {
            if (!TryTransition(out var sequence, suspended: true))
                return;

            Post("long.widget-suspend", sequence, new
            {
                reason,
                deadline_ms = deadlineMs,
            });
        }

        internal void Resume(string reason = "visible")
        {
            if (!TryTransition(out var sequence, suspended: false))
                return;

            Post("long.widget-resume", sequence, new { reason });
        }

        internal void Unmount(string reason = "disposed")
        {
            long sequence;
            lock (_gate)
            {
                if (!_context.IsWidget || _unmounted)
                    return;

                _mounted = false;
                _suspended = false;
                _unmounted = true;
                _readyTimeoutCancellation?.Cancel();
                sequence = ++_sequence;
            }

            Post("long.widget-unmount", sequence, new { reason });
        }

        internal void MarkReadyTimeout()
        {
            long sequence;
            lock (_gate)
            {
                if (_ready || _unmounted || !_mounted)
                    return;

                sequence = ++_sequence;
            }

            Post("long.widget-visibility-changed", sequence, new
            {
                visible = true,
                reason = "ready-timeout",
                ready = false,
            });
        }

        public void Dispose()
        {
            Unmount();
            _readyTimeoutCancellation?.Dispose();
        }

        private bool TryTransition(out long sequence, bool? mounted = null, bool? suspended = null)
        {
            lock (_gate)
            {
                sequence = 0;
                if (!_context.IsWidget || _unmounted)
                    return false;

                if (mounted.HasValue)
                {
                    if (_mounted == mounted.Value)
                        return false;
                    _mounted = mounted.Value;
                }

                if (suspended.HasValue)
                {
                    if (!_mounted || _suspended == suspended.Value)
                        return false;
                    _suspended = suspended.Value;
                }

                sequence = ++_sequence;
                return true;
            }
        }

        private void ScheduleReadyTimeout()
        {
            var cancellation = new CancellationTokenSource();
            lock (_gate)
            {
                _readyTimeoutCancellation?.Cancel();
                _readyTimeoutCancellation = cancellation;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_readyTimeout, cancellation.Token);
                    if (!cancellation.IsCancellationRequested)
                        MarkReadyTimeout();
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private void Post(string eventName, long sequence, object payload)
            => _postMessage(WebPluginBridgeProtocol.SerializeWidgetEvent(
                _context,
                eventName,
                sequence,
                payload));
    }

}
