using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Views;
using Serilog;

namespace LongBetterWindows.Host
{
    public partial class MainWindow : Window
    {
        private TrayService? _tray;
        private Func<Task>? _embeddedCloseRequested;
        private Action? _embeddedDetachRequested;

        public MainWindow()
        {
            InitializeComponent();
            _tray = new TrayService(this);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if ((Application.Current as App)?.ShowMarketForQualityRequested == true)
                ToolCenter.OpenMarketForQuality();

            if ((Application.Current as App)?.ShowDesignSystemPreviewRequested == true)
            {
                try
                {
                    var preview = new DesignSystemPreview { Owner = this };
                    preview.Show();
                    preview.Activate();
                    Log.Information("Long Design System 预览窗口已显示");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Long Design System 预览窗口显示失败");
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private void OpenSuperPanel_Click(object sender, RoutedEventArgs e)
            => SuperPanelWindow.ShowPanel();

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private async void Close_Click(object sender, RoutedEventArgs e)
        {
            if (EmbeddedPluginSurface.Visibility == Visibility.Visible)
                await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            Close();
        }

        internal bool IsHostingEmbedded(FrameworkElement content)
            => EmbeddedPluginSurface.Visibility == Visibility.Visible
               && ReferenceEquals(EmbeddedPluginContent.Content, content);

        internal void ShowEmbeddedPlugin(
            string title,
            FrameworkElement content,
            Func<Task> closeRequested,
            Action detachRequested)
        {
            if (EmbeddedPluginContent.Content is FrameworkElement existing
                && !ReferenceEquals(existing, content))
            {
                EmbeddedPluginContent.Content = null;
                _ = _embeddedCloseRequested?.Invoke();
            }
            EmbeddedPluginTitle.Text = title;
            EmbeddedPluginContent.Content = content;
            _embeddedCloseRequested = closeRequested;
            _embeddedDetachRequested = detachRequested;
            ToolCenter.Visibility = Visibility.Collapsed;
            EmbeddedPluginSurface.Visibility = Visibility.Visible;
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        internal void CloseEmbeddedPlugin(FrameworkElement content)
        {
            if (!ReferenceEquals(EmbeddedPluginContent.Content, content)) return;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
        }

        private async Task CloseEmbeddedSurfaceAsync(bool notifyLifecycle)
        {
            var callback = _embeddedCloseRequested;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
            if (notifyLifecycle && callback is not null)
                await callback();
        }

        private async void EmbeddedBack_Click(object sender, RoutedEventArgs e)
            => await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);

        private async void EmbeddedClose_Click(object sender, RoutedEventArgs e)
            => await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);

        private void DetachEmbedded_Click(object sender, RoutedEventArgs e)
        {
            var callback = _embeddedDetachRequested;
            EmbeddedPluginContent.Content = null;
            EmbeddedPluginSurface.Visibility = Visibility.Collapsed;
            ToolCenter.Visibility = Visibility.Visible;
            _embeddedCloseRequested = null;
            _embeddedDetachRequested = null;
            callback?.Invoke();
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape
                || EmbeddedPluginSurface.Visibility != Visibility.Visible) return;
            await CloseEmbeddedSurfaceAsync(notifyLifecycle: true);
            e.Handled = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            ServicesInitializer.HotKey.Initialize(hwnd);
            ServicesInitializer.MouseGestures.Start();
            _ = RegisterCommandPaletteHotkeyAsync();
            (Application.Current as App)?.StartPluginRuntime();
        }

        private async Task RegisterCommandPaletteHotkeyAsync()
        {
            const string preferredHotkey = "Alt+Space";
            const string fallbackHotkey = "Ctrl+Alt+Space";

            var result = await ServicesInitializer.HotKey.RegisterAsync(
                preferredHotkey,
                "builtin.command-palette",
                CommandPaletteWindow.ShowPalette);

            if (result.IsSuccess)
            {
                Log.Information("Command Palette 全局热键已注册: {Hotkey}", preferredHotkey);
                return;
            }

            Log.Warning(
                "Command Palette 首选热键 {Hotkey} 注册失败: {Error}，尝试备用热键 {Fallback}",
                preferredHotkey,
                result.ErrorMessage,
                fallbackHotkey);

            var fallback = await ServicesInitializer.HotKey.RegisterAsync(
                fallbackHotkey,
                "builtin.command-palette",
                CommandPaletteWindow.ShowPalette);

            if (fallback.IsSuccess)
                Log.Information("Command Palette 备用全局热键已注册: {Hotkey}", fallbackHotkey);
            else
                Log.Error("Command Palette 全局热键均注册失败: {Error}", fallback.ErrorMessage);
        }
    }
}
