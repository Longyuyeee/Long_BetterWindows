using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LongBetterWindows.Host.Capabilities;
using Serilog;

namespace ColorPickerPlugin;

public sealed record ColorPickerWindowLocalization(
    string Title,
    string AutomationName,
    string Instruction);

public partial class ColorPickerWindow : Window
{
    private readonly IScreenColorSampler _screenColorSampler;
    private readonly Func<string, Task> _onPicked;
    private readonly CancellationTokenSource _captureLifetime = new();
    private readonly ColorPickerPointerCapture _pointerCapture = new();
    private bool _capturing;
    private bool _closing;
    private int _selectionStarted;
    private int _failureReported;

    public event Action<string>? CaptureFailed;
    internal bool HasCommittedSelection
        => Volatile.Read(ref _selectionStarted) == 1;

    public ColorPickerWindow(
        IScreenColorSampler screenColorSampler,
        Func<string, Task> onPicked,
        ColorPickerWindowLocalization localization)
    {
        _screenColorSampler = screenColorSampler;
        _onPicked = onPicked;
        InitializeComponent();
        Cursor = Cursors.Cross;
        ApplyLocalization(localization);
    }

    public void ApplyLocalization(ColorPickerWindowLocalization localization)
    {
        Title = localization.Title;
        TitleText.Text = localization.Title;
        InstructionText.Text = localization.Instruction;
        AutomationProperties.SetName(this, localization.AutomationName);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        _pointerCapture.LeftButtonPressed += PointerCapture_LeftButtonPressed;
        if (!_pointerCapture.TryStart(out var error))
        {
            FailAndClose(error?.Message ?? "Unable to capture the next screen click.");
            return;
        }
        _capturing = true;
        _ = CaptureLoopAsync();
    }

    private async Task CaptureLoopAsync()
    {
        var cancellationToken = _captureLifetime.Token;
        while (_capturing && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!ColorPickerNativeWindow.TryGetCursorPosition(out var point))
                {
                    FailAndClose("Unable to read the physical cursor position.");
                    return;
                }
                if (!UpdateSample(point))
                {
                    FailAndClose("The desktop pixel could not be sampled.");
                    return;
                }
                ColorPickerNativeWindow.PositionNearCursor(this, point);
                await Task.Delay(35, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ColorPicker] Capture loop iteration failed");
                FailAndClose(ex.Message);
                return;
            }
        }
    }

    private bool UpdateSample(ColorPickerPhysicalPoint point)
    {
        try
        {
            var result = _screenColorSampler.Sample(point.X, point.Y);
            if (!result.IsSuccess || result.Data is null)
            {
                Log.Warning(
                    "[ColorPicker] Screen pixel sampling failed: {Error}",
                    result.ErrorMessage ?? result.ErrorCode.ToString());
                return false;
            }
            var color = result.Data;
            ColorBox.Background = new SolidColorBrush(Color.FromRgb(
                color.Red,
                color.Green,
                color.Blue));
            HexText.Text = color.Hex;
            RgbText.Text = $"rgb({color.Red}, {color.Green}, {color.Blue})";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ColorPicker] Screen pixel sampling failed");
            return false;
        }
    }

    private void PointerCapture_LeftButtonPressed(
        ColorPickerPhysicalPoint point)
    {
        if (Interlocked.CompareExchange(ref _selectionStarted, 1, 0) != 0)
            return;
        _ = Dispatcher.InvokeAsync(
            () => _ = PickAndCloseAsync(point),
            DispatcherPriority.Input);
    }

    private async Task PickAndCloseAsync(ColorPickerPhysicalPoint point)
    {
        _capturing = false;
        try
        {
            if (!UpdateSample(point))
            {
                FailAndClose("The selected desktop pixel could not be sampled.");
                return;
            }

            var selectedHex = HexText.Text;
            Hide();
            await _pointerCapture.WaitForLeftButtonReleaseAsync(
                _captureLifetime.Token);
            _captureLifetime.Token.ThrowIfCancellationRequested();
            ClosePicker();
            await _onPicked(selectedHex);
        }
        catch (OperationCanceledException)
            when (_captureLifetime.IsCancellationRequested)
        {
            // Closing or stopping the picker cancels an uncommitted click.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ColorPicker] Selected color delivery failed");
            FailAndClose(ex.Message);
        }
        finally
        {
            ClosePicker();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (Interlocked.CompareExchange(ref _selectionStarted, 2, 0) != 0)
            return;
        _capturing = false;
        ClosePicker();
        e.Handled = true;
    }

    private void FailAndClose(string message)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) == 0)
            CaptureFailed?.Invoke(message);
        ClosePicker();
    }

    private void ClosePicker()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        _capturing = false;
        _captureLifetime.Cancel();
        _pointerCapture.LeftButtonPressed -= PointerCapture_LeftButtonPressed;
        _pointerCapture.Dispose();
    }
}
