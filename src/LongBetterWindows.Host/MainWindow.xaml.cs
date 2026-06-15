using System.Windows;
using System.Windows.Interop;
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            ServicesInitializer.HotKey.Initialize(hwnd);
        }
    }
}
