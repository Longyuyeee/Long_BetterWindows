using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.PluginSdk.Wpf;

namespace ScreenshotPlugin;

public sealed record RegionSelectorLocalization(
    string AutomationName,
    string Instruction,
    string Cancel);

public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private ScreenshotPhysicalPoint _screenStart;
    private ScreenshotPhysicalPoint _screenEnd;
    private bool _drawing;
    private bool _closing;
    private int _selectionCommitted;

    public event Func<Int32Rect, Task>? RegionSelected;
    public event Action<Exception>? CaptureFailed;
    internal bool HasCommittedSelection
        => Volatile.Read(ref _selectionCommitted) == 1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public RegionSelectorWindow(RegionSelectorLocalization localization)
    {
        InitializeComponent();
        ApplyLocalization(localization);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            VirtualScreenHelper.PlaceWindowOverPhysicalBounds(this);
            Activate();
            Focus();
            Keyboard.Focus(SelectionCanvas);
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(ex);
            CloseSelector();
        }
    }

    public void ApplyLocalization(RegionSelectorLocalization localization)
    {
        AutomationProperties.SetName(SelectionCanvas, localization.AutomationName);
        InstructionText.Text = localization.Instruction;
        CancelText.Text = "  " + localization.Cancel;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_drawing || Volatile.Read(ref _selectionCommitted) != 0)
            return;
        if (!TryGetCursorPosition(out _screenStart))
            return;
        _screenEnd = _screenStart;
        _start = PointFromScreen(new Point(_screenStart.X, _screenStart.Y));
        _drawing = true;
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        InstructionCard.Visibility = Visibility.Collapsed;
        UpdateSelection();
        SelectionCanvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drawing)
            UpdateSelection();
    }

    private async void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        if (!UpdateSelection())
        {
            CloseSelector();
            return;
        }
        _drawing = false;
        SelectionCanvas.ReleaseMouseCapture();

        if (!ScreenshotRegionGeometry.TryCreate(
                VirtualScreenHelper.GetPhysicalBounds(),
                _screenStart,
                _screenEnd,
                out var bounds)
            || Interlocked.CompareExchange(
                ref _selectionCommitted,
                1,
                0) != 0)
        {
            CloseSelector();
            return;
        }

        try
        {
            CloseSelector();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (RegionSelected is not null)
                await RegionSelected(bounds);
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(ex);
        }
    }

    private bool UpdateSelection()
    {
        if (!TryGetCursorPosition(out _screenEnd))
            return false;
        var end = PointFromScreen(new Point(_screenEnd.X, _screenEnd.Y));
        var x = Math.Min(_start.X, end.X);
        var y = Math.Min(_start.Y, end.Y);
        var width = Math.Abs(end.X - _start.X);
        var height = Math.Abs(end.Y - _start.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
        SizeText.Text =
            $"{Math.Abs((long)_screenEnd.X - _screenStart.X) + 1} × " +
            $"{Math.Abs((long)_screenEnd.Y - _screenStart.Y) + 1}";
        Canvas.SetLeft(SizeBadge, Math.Min(x + width + 8, Math.Max(0, ActualWidth - 100)));
        Canvas.SetTop(SizeBadge, Math.Min(y + height + 8, Math.Max(0, ActualHeight - 40)));
        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if (Interlocked.CompareExchange(ref _selectionCommitted, 2, 0) != 0)
            return;
        _drawing = false;
        if (SelectionCanvas.IsMouseCaptured)
            SelectionCanvas.ReleaseMouseCapture();
        CloseSelector();
        e.Handled = true;
    }

    private static bool TryGetCursorPosition(
        out ScreenshotPhysicalPoint point)
    {
        if (GetCursorPos(out var nativePoint))
        {
            point = new ScreenshotPhysicalPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private void CloseSelector()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        _drawing = false;
        if (SelectionCanvas.IsMouseCaptured)
            SelectionCanvas.ReleaseMouseCapture();
    }
}
