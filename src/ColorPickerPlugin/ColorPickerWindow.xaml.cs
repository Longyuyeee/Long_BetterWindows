using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using LongBetterWindows.Host.Services;
using Serilog;

namespace ColorPickerPlugin;

public sealed record ColorPickerWindowLocalization(
    string Title,
    string AutomationName,
    string Instruction);

public partial class ColorPickerWindow : Window
{
    private readonly Func<string, Task> _onPicked;
    private readonly CancellationTokenSource _captureLifetime = new();
    private bool _capturing;
    private bool _leftWasDown;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative { public int X; public int Y; }

    public ColorPickerWindow(
        Func<string, Task> onPicked,
        ColorPickerWindowLocalization localization)
    {
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
        _leftWasDown = IsLeftButtonDown();
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
                if (!GetCursorPos(out var point))
                {
                    Close();
                    return;
                }
                if (!UpdateSample(point))
                {
                    Close();
                    return;
                }
                PositionNearCursor();

                var leftDown = IsLeftButtonDown();
                if (leftDown && !_leftWasDown)
                {
                    await PickAndCloseAsync();
                    return;
                }
                _leftWasDown = leftDown;
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
                await Task.Delay(80, cancellationToken);
            }
        }
    }

    private bool UpdateSample(PointNative point)
    {
        try
        {
            var color = ScreenColorSampler.Sample(point.X, point.Y);
            ColorBox.Background = new SolidColorBrush(color);
            HexText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            RgbText.Text = $"rgb({color.R}, {color.G}, {color.B})";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ColorPicker] Screen pixel sampling failed");
            return false;
        }
    }

    private void PositionNearCursor()
    {
        var placement =
            MonitorHelper.GetCursorPlacement(this);
        Left = Math.Clamp(
            placement.Cursor.X + 18,
            placement.WorkArea.Left + 8,
            placement.WorkArea.Right - Width - 8);
        Top = Math.Clamp(
            placement.Cursor.Y + 18,
            placement.WorkArea.Top + 8,
            placement.WorkArea.Bottom - Height - 8);
    }

    private async Task PickAndCloseAsync()
    {
        _capturing = false;
        try
        {
            await _onPicked(HexText.Text);
        }
        finally
        {
            Close();
        }
    }

    private static bool IsLeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _capturing = false;
        Close();
        e.Handled = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _capturing = false;
        _captureLifetime.Cancel();
    }
}
