using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ScreenshotPlugin;

public sealed record RegionSelectorLocalization(
    string AutomationName,
    string Instruction,
    string Cancel);

public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private PointNative _screenStart;
    private PointNative _screenEnd;
    private bool _drawing;

    public event Func<Int32Rect, Task>? RegionSelected;
    public event Action<Exception>? CaptureFailed;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointNative point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    public RegionSelectorWindow(RegionSelectorLocalization localization)
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        ApplyLocalization(localization);
    }

    public void ApplyLocalization(RegionSelectorLocalization localization)
    {
        AutomationProperties.SetName(SelectionCanvas, localization.AutomationName);
        InstructionText.Text = localization.Instruction;
        CancelText.Text = "  " + localization.Cancel;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out _screenStart))
            return;
        _screenEnd = _screenStart;
        _start = e.GetPosition(SelectionCanvas);
        _drawing = true;
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        InstructionCard.Visibility = Visibility.Collapsed;
        UpdateSelection(_start);
        SelectionCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drawing)
            UpdateSelection(e.GetPosition(SelectionCanvas));
    }

    private async void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        UpdateSelection(e.GetPosition(SelectionCanvas));
        _drawing = false;
        SelectionCanvas.ReleaseMouseCapture();

        if (SelectionRectangle.Width <= 5 || SelectionRectangle.Height <= 5)
        {
            Close();
            return;
        }

        var bounds = new Int32Rect(
            Math.Min(_screenStart.X, _screenEnd.X),
            Math.Min(_screenStart.Y, _screenEnd.Y),
            Math.Abs(_screenEnd.X - _screenStart.X),
            Math.Abs(_screenEnd.Y - _screenStart.Y));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Close();
            return;
        }

        try
        {
            Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (RegionSelected is not null)
                await RegionSelected(bounds);
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(ex);
        }
    }

    private void UpdateSelection(Point end)
    {
        if (GetCursorPos(out var screenEnd))
            _screenEnd = screenEnd;
        var x = Math.Min(_start.X, end.X);
        var y = Math.Min(_start.Y, end.Y);
        var width = Math.Abs(end.X - _start.X);
        var height = Math.Abs(end.Y - _start.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        SizeText.Text =
            $"{Math.Abs(_screenEnd.X - _screenStart.X)} × " +
            $"{Math.Abs(_screenEnd.Y - _screenStart.Y)}";
        Canvas.SetLeft(SizeBadge, Math.Min(x + width + 8, Math.Max(0, ActualWidth - 100)));
        Canvas.SetTop(SizeBadge, Math.Min(y + height + 8, Math.Max(0, ActualHeight - 40)));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
