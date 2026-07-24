using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace ColorPickerPlugin;

public sealed record ColorPickerWindowLocalization(
    string Title,
    string AutomationName,
    string Instruction);

public partial class ColorPickerWindow : Window
{
    private readonly Func<string, Task> _onPicked;
    private bool _capturing;
    private bool _leftWasDown;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
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
        while (_capturing)
        {
            try
            {
                if (!GetCursorPos(out var point)) break;
                UpdateSample(point);
                PositionNearCursor(point);

                var leftDown = IsLeftButtonDown();
                if (leftDown && !_leftWasDown)
                {
                    await PickAndCloseAsync();
                    return;
                }
                _leftWasDown = leftDown;
                await Task.Delay(35);
            }
            catch
            {
                await Task.Delay(80);
            }
        }
    }

    private void UpdateSample(PointNative point)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return;
        try
        {
            var pixel = GetPixel(dc, point.X, point.Y);
            var red = (byte)(pixel & 0xFF);
            var green = (byte)((pixel >> 8) & 0xFF);
            var blue = (byte)((pixel >> 16) & 0xFF);
            ColorBox.Background = new SolidColorBrush(Color.FromRgb(red, green, blue));
            HexText.Text = $"#{red:X2}{green:X2}{blue:X2}";
            RgbText.Text = $"rgb({red}, {green}, {blue})";
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private void PositionNearCursor(PointNative point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = point.X / dpi.DpiScaleX;
        var y = point.Y / dpi.DpiScaleY;
        var area = LongBetterWindows.Host.Services.MonitorHelper.GetCursorWorkArea();
        Left = Math.Clamp(x + 18, area.Left + 8, area.Right - Width - 8);
        Top = Math.Clamp(y + 18, area.Top + 8, area.Bottom - Height - 8);
    }

    private async Task PickAndCloseAsync()
    {
        _capturing = false;
        await _onPicked(HexText.Text);
        Close();
    }

    private static bool IsLeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _capturing = false;
        Close();
        e.Handled = true;
    }

    private void Window_Closed(object? sender, EventArgs e) => _capturing = false;
}
