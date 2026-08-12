using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views;

public partial class PluginContentWindow : Window
{
    internal PluginContentWindow(
        string title,
        double width,
        double height,
        bool resizable,
        WebView2 webView)
    {
        InitializeComponent();
        Title = title;
        Width = width;
        Height = height;
        MinWidth = Math.Min(320, width);
        MinHeight = Math.Min(240, height);
        ResizeMode = resizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        AutomationProperties.SetName(this, title);
        WebViewHost.Children.Add(webView);

        Loaded += (_, _) => webView.Focus();
        webView.PreviewKeyDown += WebView_PreviewKeyDown;
        Closed += (_, _) => webView.PreviewKeyDown -= WebView_PreviewKeyDown;
    }

    private void WebView_PreviewKeyDown(object sender, KeyEventArgs e)
        => CloseOnEscape(e);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        => CloseOnEscape(e);

    private void CloseOnEscape(KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
