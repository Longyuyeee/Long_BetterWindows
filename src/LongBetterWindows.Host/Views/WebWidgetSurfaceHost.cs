using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Engine;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    /// <summary>
    /// WPF boundary for one Web Widget. The parent surface owns grid placement;
    /// this control reports actual pixels, monitor DPI, and visibility to LPWP.
    /// </summary>
    internal sealed class WebWidgetSurfaceHost : ContentControl, IDisposable
    {
        private readonly WebWidgetSurfaceSession _session;
        private Task<bool>? _initialization;
        private int _columns;
        private int _rows;
        private bool _disposed;

        internal WebWidgetSurfaceHost(
            WebWidgetSurfaceSession session,
            int columns,
            int rows)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            ValidateGridSize(columns, rows);
            _columns = columns;
            _rows = rows;

            Focusable = false;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
            SizeChanged += OnSizeChanged;
        }

        internal Task<bool>? Initialization => _initialization;

        internal void SetGridSize(int columns, int rows)
        {
            ThrowIfDisposed();
            ValidateGridSize(columns, rows);
            if (_columns == columns && _rows == rows)
                return;
            _columns = columns;
            _rows = rows;
            PublishLayout();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            PublishLayout(newDpi);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            IsVisibleChanged -= OnIsVisibleChanged;
            SizeChanged -= OnSizeChanged;
            Content = null;
            _session.Dispose();
        }

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            if (_disposed)
                return;

            PublishLayout();
            Content ??= _session.EnsureView();
            _session.SetVisible(IsVisible, "surface-loaded");
            try
            {
                _initialization ??= _session.InitializeAsync();
                if (!await _initialization)
                    Log.Warning(
                        "Widget surface {PluginId}:{WidgetId}:{InstanceId} failed to initialize.",
                        _session.PluginId,
                        _session.WidgetId,
                        _session.InstanceId);
            }
            catch (Exception exception)
            {
                Log.Error(
                    exception,
                    "Widget surface {PluginId}:{WidgetId}:{InstanceId} initialization failed.",
                    _session.PluginId,
                    _session.WidgetId,
                    _session.InstanceId);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            if (!_disposed)
                _session.SetVisible(false, "surface-unloaded");
        }

        private void OnIsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs args)
        {
            if (!_disposed && IsLoaded)
            {
                _session.SetVisible(
                    IsVisible,
                    IsVisible ? "surface-visible" : "surface-hidden");
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
            => PublishLayout();

        private void PublishLayout(DpiScale? dpi = null)
        {
            if (_disposed)
                return;

            var currentDpi = dpi ?? VisualTreeHelper.GetDpi(this);
            if (WidgetSurfaceLayout.TryFromLogicalSize(
                    _columns,
                    _rows,
                    ActualWidth,
                    ActualHeight,
                    currentDpi.DpiScaleX,
                    currentDpi.DpiScaleY,
                    out var layout))
            {
                _session.UpdateLayout(layout);
            }
        }

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(_disposed, this);

        private static void ValidateGridSize(int columns, int rows)
        {
            if (columns is < 1 or > 24 || rows is < 1 or > 24)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    "Widget grid dimensions must be between 1 and 24.");
            }
        }
    }
}
