using System.Windows;
using System.Windows.Interop;
using LongBetterWindows.Host.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LongBetterWindows.Host
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            SystemThemeWatcher.Watch(this);
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            ServicesInitializer.HotKey.Initialize(hwnd);
        }
    }
}
