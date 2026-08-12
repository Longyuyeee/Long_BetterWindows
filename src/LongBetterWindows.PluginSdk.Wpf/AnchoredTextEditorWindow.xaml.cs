using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.PluginSdk.Wpf;

public sealed record AnchoredTextEditorLocalization(
    string Title,
    string InputAutomationName,
    string EmptyHint,
    string ModifiedHint);

public partial class AnchoredTextEditorWindow : Window
{
    private readonly Func<string, Task> _onSave;
    private AnchoredTextEditorLocalization _localization;
    private bool _dirty;
    private bool _isClosing;
    private CancellationTokenSource? _closeDelay;

    private AnchoredTextEditorWindow(
        Func<string, Task> onSave,
        AnchoredTextEditorLocalization localization)
    {
        _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
        _localization = localization;
        InitializeComponent();
        ApplyLocalization(localization);
    }

    public static AnchoredTextEditorWindow ShowAt(
        double x,
        double y,
        string? initialText,
        string? contextLabel,
        Func<string, Task> onSave,
        AnchoredTextEditorLocalization localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        var window = new AnchoredTextEditorWindow(onSave, localization)
        {
            Left = x,
            Top = y,
        };
        window.ContextLabel.Text = contextLabel ?? string.Empty;
        window.Editor.Text = initialText ?? string.Empty;
        window._dirty = false;
        window.RenderHint();
        window.Show();
        window.PlaceAtPhysicalAnchor(x, y);
        window.Editor.Focus();
        window.Editor.CaretIndex = window.Editor.Text.Length;
        return window;
    }

    private void PlaceAtPhysicalAnchor(double physicalX, double physicalY)
    {
        var transform = PresentationSource
            .FromVisual(this)?
            .CompositionTarget?
            .TransformFromDevice
            ?? Matrix.Identity;
        var logicalAnchor = transform.Transform(new Point(physicalX, physicalY));
        Left = logicalAnchor.X;
        Top = logicalAnchor.Y;
    }

    public void ApplyLocalization(AnchoredTextEditorLocalization localization)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        TitleLabel.Text = localization.Title;
        AutomationProperties.SetName(Editor, localization.InputAutomationName);
        RenderHint();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateOpacity(0, 1, "Long.Motion.Normal", 350, closeOnComplete: false);
        var duration = Motion("Long.Motion.Slow", 400);
        if (duration == TimeSpan.Zero)
            return;

        var scale = new ScaleTransform(0.92, 0.92);
        RenderTransform = scale;
        RenderTransformOrigin = new Point(0.5, 0.5);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, duration) { EasingFunction = easing });
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _closeDelay?.Cancel();
        _closeDelay?.Dispose();
        _closeDelay = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(_closeDelay.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(3000, cancellationToken);
            await Dispatcher.InvokeAsync(SaveAndCloseAsync).Task.Unwrap();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Window_Activated(object sender, EventArgs e)
        => _closeDelay?.Cancel();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _dirty = false;
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _closeDelay?.Cancel();
            await SaveAndCloseAsync();
            e.Handled = true;
        }
    }

    private void Editor_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _dirty = true;
        RenderHint();
    }

    private async Task SaveAndCloseAsync()
    {
        if (_isClosing)
            return;
        _isClosing = true;

        try
        {
            if (_dirty)
                await _onSave(Editor.Text);
            AnimateOpacity(1, 0, "Long.Motion.Fast", 150, closeOnComplete: true);
        }
        catch (Exception exception)
        {
            _isClosing = false;
            HintText.Text = exception.Message;
            HintText.SetResourceReference(
                ForegroundProperty,
                "Long.Brush.State.Danger");
            Editor.Focus();
        }
    }

    private void RenderHint()
    {
        if (HintText is null)
            return;
        HintText.Text = _dirty
            ? _localization.ModifiedHint
            : _localization.EmptyHint;
        HintText.SetResourceReference(
            ForegroundProperty,
            "Long.Brush.Text.Muted");
    }

    private void AnimateOpacity(
        double from,
        double to,
        string durationKey,
        int fallbackMs,
        bool closeOnComplete)
    {
        var duration = Motion(durationKey, fallbackMs);
        if (duration == TimeSpan.Zero)
        {
            Opacity = to;
            if (closeOnComplete)
                Close();
            return;
        }

        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase
            {
                EasingMode = to > from
                    ? EasingMode.EaseOut
                    : EasingMode.EaseIn,
            },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            Opacity = to;
            if (closeOnComplete)
                Close();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private static TimeSpan Motion(string key, int fallbackMs)
        => Application.Current?.Resources[key] is Duration duration
            ? duration.TimeSpan
            : TimeSpan.FromMilliseconds(fallbackMs);

    protected override void OnClosed(EventArgs e)
    {
        _closeDelay?.Cancel();
        _closeDelay?.Dispose();
        _closeDelay = null;
        base.OnClosed(e);
    }
}
