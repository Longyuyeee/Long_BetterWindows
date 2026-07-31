using LongBetterWindows.Host.Contracts;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// Owns one isolated Web Widget instance without borrowing normal plugin
    /// window placement or workspace-session state.
    /// </summary>
    internal sealed class WebWidgetSurfaceSession : IDisposable
    {
        internal static readonly TimeSpan DefaultHiddenSuspendDelay =
            TimeSpan.FromSeconds(30);

        private readonly WebPluginRuntime _runtime;
        private readonly PluginWidgetHiddenBehavior _hiddenBehavior;
        private readonly TimeSpan _hiddenSuspendDelay;
        private readonly object _gate = new();
        private CancellationTokenSource? _hiddenSuspendCancellation;
        private bool _initialized;
        private bool _visible = true;
        private bool _disposed;

        internal WebWidgetSurfaceSession(
            PluginManifest manifest,
            string pluginDirectory,
            PluginWidgetDefinition widget,
            string instanceId,
            WidgetSurfaceLayout initialLayout,
            TimeSpan? hiddenSuspendDelay = null)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(widget);
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Widget instance id is required.", nameof(instanceId));
            if (!string.Equals(manifest.Runtime, "webview", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Web Widget requires a webview plugin.", nameof(manifest));
            var declaredWidget = manifest.Widgets.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, widget.Id, StringComparison.Ordinal));
            if (declaredWidget is null)
            {
                throw new ArgumentException(
                    "Widget definition does not belong to the plugin manifest.",
                    nameof(widget));
            }

            ValidateLayout(initialLayout);
            if (hiddenSuspendDelay is { } delay && delay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(hiddenSuspendDelay));
            PluginId = manifest.Id;
            WidgetId = declaredWidget.Id;
            InstanceId = instanceId;
            EntryPoint = declaredWidget.EntryPoint;
            _hiddenBehavior = declaredWidget.Refresh?.HiddenBehavior
                ?? PluginWidgetHiddenBehavior.Suspend;
            _hiddenSuspendDelay = hiddenSuspendDelay ?? DefaultHiddenSuspendDelay;
            _runtime = new WebPluginRuntime(
                manifest,
                pluginDirectory,
                new WebPluginBridgeContext(
                    manifest.Id,
                    surface: "widget",
                    widgetId: declaredWidget.Id,
                    instanceId: instanceId),
                declaredWidget.EntryPoint,
                initialLayout);
        }

        internal string PluginId { get; }
        internal string WidgetId { get; }
        internal string InstanceId { get; }
        internal string EntryPoint { get; }
        internal WebView2? View => _runtime.WebView;

        internal WebView2 EnsureView() => _runtime.EnsureView();

        internal async Task<bool> InitializeAsync()
        {
            ThrowIfDisposed();
            var initialized = await _runtime.InitializeAsync();
            if (!initialized)
                return false;

            bool visible;
            lock (_gate)
            {
                _initialized = true;
                visible = _visible;
            }

            if (!visible)
            {
                _runtime.NotifyWidgetVisibilityChanged(false, "surface-hidden");
                if (_hiddenBehavior == PluginWidgetHiddenBehavior.Suspend)
                    ScheduleHiddenSuspend();
            }
            return true;
        }

        internal void UpdateLayout(WidgetSurfaceLayout layout)
        {
            ThrowIfDisposed();
            ValidateLayout(layout);
            _runtime.NotifyWidgetLayoutChanged(layout);
        }

        internal void SetVisible(bool visible, string reason)
        {
            ThrowIfDisposed();
            bool initialized;
            lock (_gate)
            {
                if (_visible == visible)
                    return;
                _visible = visible;
                CancelHiddenSuspend();
                initialized = _initialized;
            }

            if (!initialized)
                return;
            _runtime.NotifyWidgetVisibilityChanged(visible, reason);
            if (visible)
            {
                _runtime.ResumeWidget(reason);
                return;
            }

            if (_hiddenBehavior == PluginWidgetHiddenBehavior.Suspend)
                ScheduleHiddenSuspend();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                CancelHiddenSuspend();
            }

            _runtime.Dispose();
        }

        private void ScheduleHiddenSuspend()
        {
            var cancellation = new CancellationTokenSource();
            var cancellationToken = cancellation.Token;
            lock (_gate)
            {
                if (_disposed || _visible)
                {
                    cancellation.Dispose();
                    return;
                }
                CancelHiddenSuspend();
                _hiddenSuspendCancellation = cancellation;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_hiddenSuspendDelay, cancellationToken);
                    lock (_gate)
                    {
                        if (_disposed || _visible || cancellationToken.IsCancellationRequested)
                            return;
                    }
                    _runtime.SuspendWidget(
                        "hidden",
                        checked((int)Math.Min(
                            int.MaxValue,
                            _hiddenSuspendDelay.TotalMilliseconds)));
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private void CancelHiddenSuspend()
        {
            _hiddenSuspendCancellation?.Cancel();
            _hiddenSuspendCancellation?.Dispose();
            _hiddenSuspendCancellation = null;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static void ValidateLayout(WidgetSurfaceLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            if (!layout.IsValid)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    "Widget layout must contain valid grid, pixel, and DPI dimensions.");
            }
        }
    }
}
