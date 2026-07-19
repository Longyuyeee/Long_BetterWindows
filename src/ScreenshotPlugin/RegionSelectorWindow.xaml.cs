using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ScreenshotPlugin;

public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _drawing;

    public event Action<BitmapSource>? RegionSelected;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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

        var screenX = (int)(SystemParameters.VirtualScreenLeft + Canvas.GetLeft(SelectionRectangle));
        var screenY = (int)(SystemParameters.VirtualScreenTop + Canvas.GetTop(SelectionRectangle));
        var bitmap = ScreenCapture.Capture(
            screenX, screenY,
            Math.Max(1, (int)SelectionRectangle.Width),
            Math.Max(1, (int)SelectionRectangle.Height));
        RegionSelected?.Invoke(bitmap);
        Close();
    }

    private void UpdateSelection(Point end)
    {
        var x = Math.Min(_start.X, end.X);
        var y = Math.Min(_start.Y, end.Y);
        var width = Math.Abs(end.X - _start.X);
        var height = Math.Abs(end.Y - _start.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        SizeText.Text = $"{(int)width} × {(int)height}";
        Canvas.SetLeft(SizeBadge, Math.Min(x + width + 8, Math.Max(0, ActualWidth - 100)));
        Canvas.SetTop(SizeBadge, Math.Min(y + height + 8, Math.Max(0, ActualHeight - 40)));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
