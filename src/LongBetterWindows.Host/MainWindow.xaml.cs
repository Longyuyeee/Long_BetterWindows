using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host
{
    public partial class MainWindow : Window
    {
        private TrayService? _tray;

        public MainWindow()
        {
            InitializeComponent();
            _tray = new TrayService(this);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AnimationHelper.FadeIn(this, durationMs: 350);

            // 启用 Acrylic 毛玻璃效果
            AcrylicHelper.EnableAcrylic(this, 0xCC1E1F22);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            ServicesInitializer.HotKey.Initialize(hwnd);
        }
    }
}
