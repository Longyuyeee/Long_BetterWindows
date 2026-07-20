using System.Windows;
using System.Windows.Input;

namespace LongBetterWindows.Host.Views
{
    public partial class DesignSystemPreview : Window
    {
        public DesignSystemPreview()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
