using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace LongBetterWindows.Host.Views;

public partial class CapabilitySelectDialog : Window
{
    internal bool Confirmed { get; private set; }
    internal int SelectedIndex => OptionsList.SelectedIndex;

    internal CapabilitySelectDialog(
        Window? owner,
        string message,
        IEnumerable<string> options,
        string title)
    {
        InitializeComponent();
        Owner = owner;
        if (owner is null)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = title;
        MessageText.Text = message;
        AutomationProperties.SetName(this, title);
        foreach (var option in options)
            OptionsList.Items.Add(option);
        if (OptionsList.Items.Count > 0)
            OptionsList.SelectedIndex = 0;
        Loaded += (_, _) => OptionsList.Focus();
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
