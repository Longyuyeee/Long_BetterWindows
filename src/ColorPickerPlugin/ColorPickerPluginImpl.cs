using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LongBetterWindows.Host.Core;
using Serilog;

namespace ColorPickerPlugin;

public class ColorPickerPluginImpl : ILongPlugin, IHasSettingsUI, IHasMainUI
{
    private IHostApi? _host;
    private string _hotkey = "Ctrl+Shift+P";

    public string Id => "com.long.color-picker";
    public string Name => "颜色拾取器";
    public string Version => "1.0.0";
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        if (host.HotKey == null)
        { State = PluginState.Error; return Task.FromResult(false); }
        return Task.FromResult(true);
    }

    public async Task<bool> StartAsync()
    {
        var r = await _host!.HotKey!.RegisterAsync(_hotkey, Id, OnPickColor);
        if (!r.IsSuccess) { State = PluginState.Error; return false; }
        State = PluginState.Running;
        return true;
    }

    public async Task<bool> StopAsync()
    {
        await _host!.HotKey!.UnregisterAsync(_hotkey);
        State = PluginState.Disabled;
        return true;
    }

    private void OnPickColor()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var picker = new ColorPickerWindow();
            picker.Show();
        });
    }

    public FrameworkElement CreateSettingsUI()
    {
        return new LongBetterWindows.Host.Views.HotkeySettingsControl(
            "颜色拾取器", Id, _hotkey, hk => _hotkey = hk);
    }

    public void ShowMainUI() => OnPickColor();
}

/// <summary>
/// 屏幕颜色拾取窗口——放大镜 + 颜色显示
/// </summary>
public class ColorPickerWindow : Window
{
    private bool _capturing;
    private Border _colorBox = null!;
    private TextBlock _hexText = null!;
    private TextBlock _rgbText = null!;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }

    public ColorPickerWindow()
    {
        Width = 200; Height = 220;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = System.Windows.Input.Cursors.Cross;

        _colorBox = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0,0,0,8), Background = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center };
        _hexText = new TextBlock { Text = "#000000", FontSize = 14,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        _rgbText = new TextBlock { Text = "rgb(0,0,0)", FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0,0xA0,0xA0)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,6) };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1D, 0x1D, 0x1F)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "颜色拾取器", FontSize = 13, FontWeight = FontWeights.Medium,
                        Foreground = Brushes.White, Margin = new Thickness(0,0,0,10) },
                    _colorBox, _hexText, _rgbText,
                    new TextBlock { Text = "点击: 复制  Esc: 取消", FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x80,0x80,0x80)),
                        HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };

        Loaded += (_, _) => StartCapture();
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        MouseLeftButtonDown += (_, _) => PickAndClose();
    }

    private async void StartCapture()
    {
        _capturing = true;
        while (_capturing)
        {
            try
            {
            GetCursorPos(out var pt);
            Left = pt.X + 16; Top = pt.Y + 16;

            var dc = GetDC(IntPtr.Zero);
            var pixel = GetPixel(dc, pt.X, pt.Y);
            ReleaseDC(IntPtr.Zero, dc);

            var r = (byte)(pixel & 0xFF);
            var g = (byte)((pixel >> 8) & 0xFF);
            var b = (byte)((pixel >> 16) & 0xFF);
            var color = Color.FromRgb(r, g, b);

            _colorBox.Background = new SolidColorBrush(color);
            _hexText.Text = $"#{r:X2}{g:X2}{b:X2}";
            _rgbText.Text = $"rgb({r},{g},{b})";

            await Task.Delay(50);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ColorPicker capture error: {ex.Message}");
            }
        }
    }

    private void PickAndClose()
    {
        _capturing = false;
        try { Clipboard.SetText(_hexText.Text); } catch { }
        Close();
    }
}
