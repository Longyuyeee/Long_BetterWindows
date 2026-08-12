using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Views;

public partial class PluginHtmlPreview : Window
{
    private readonly WebView2 _webView = new();
    private readonly Uri _source;

    public PluginHtmlPreview(Window owner, string title, string path)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        _source = new Uri(path);
        WebViewHost.Children.Add(_webView);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.PreviewKeyDown += WebView_PreviewKeyDown;
        _webView.CoreWebView2.Navigate(_source.AbsoluteUri);
        _webView.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _webView.PreviewKeyDown -= WebView_PreviewKeyDown;
        _webView.Dispose();
    }

    private void WebView_PreviewKeyDown(object sender, KeyEventArgs e)
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
