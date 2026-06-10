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
    }
}
