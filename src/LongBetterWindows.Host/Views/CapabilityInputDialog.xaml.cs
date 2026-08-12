using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace LongBetterWindows.Host.Views;

public partial class CapabilityInputDialog : Window
{
    internal bool Confirmed { get; private set; }
    internal string InputText => InputBox.Text;

    internal CapabilityInputDialog(
        Window? owner,
        string message,
        string title,
        string defaultValue)
    {
        InitializeComponent();
        Owner = owner;
        if (owner is null)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = title;
        MessageText.Text = message;
        InputBox.Text = defaultValue;
        AutomationProperties.SetName(this, title);
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Cancel();
    }

    private void Cancel()
    {
        Confirmed = false;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
