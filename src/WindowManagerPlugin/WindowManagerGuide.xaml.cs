using System.Windows;

namespace WindowManagerPlugin;

public partial class WindowManagerGuide : Window
{
    public WindowManagerGuide() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
