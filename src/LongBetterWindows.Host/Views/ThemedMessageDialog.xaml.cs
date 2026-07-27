using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LongBetterWindows.Host.Views;

internal enum ThemedMessageDialogTone
{
    Info,
    Success,
    Warning,
    Danger,
}

internal sealed record ThemedMessageDialogThemeSnapshot(
    Color Surface,
    Color Title,
    Color Message,
    Color PrimaryBackground,
    Color PrimaryForeground,
    Color SecondaryBackground,
    Color SecondaryForeground);

public partial class ThemedMessageDialog : Window
{
    private bool _confirmed;

    private ThemedMessageDialog(
        string message,
        string title,
        bool confirmation,
        ThemedMessageDialogTone tone)
    {
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        CancelButton.Visibility = confirmation
            ? Visibility.Visible
            : Visibility.Collapsed;
        PrimaryButton.Content = Services.ServicesInitializer.I18n.T(
            confirmation ? "action.confirm" : "action.ok");
        ApplyTone(tone);
        Loaded += (_, _) =>
        {
            if (confirmation)
                CancelButton.Focus();
            else
                PrimaryButton.Focus();
        };
    }

    internal static void ShowAlert(
        Window? owner,
        string message,
        string title,
        ThemedMessageDialogTone tone = ThemedMessageDialogTone.Info)
        => Create(owner, message, title, confirmation: false, tone).ShowDialog();

    internal static bool ShowConfirmation(
        Window? owner,
        string message,
        string title,
        ThemedMessageDialogTone tone = ThemedMessageDialogTone.Warning)
    {
        var dialog = Create(owner, message, title, confirmation: true, tone);
        dialog.ShowDialog();
        return dialog._confirmed;
    }

    internal static ThemedMessageDialog CreateForQuality()
        => Create(
            owner: null,
            "This action changes protected workflow data. Review the details before continuing.",
            "Confirm protected action",
            confirmation: true,
            ThemedMessageDialogTone.Warning);

    internal ThemedMessageDialogThemeSnapshot GetThemeSnapshot()
        => new(
            GetColor(DialogSurface.Background),
            GetColor(DialogTitle.Foreground),
            GetColor(DialogMessage.Foreground),
            GetColor(PrimaryButton.Background),
            GetColor(PrimaryButton.Foreground),
            GetColor(CancelButton.Background),
            GetColor(CancelButton.Foreground));

    private static ThemedMessageDialog Create(
        Window? owner,
        string message,
        string title,
        bool confirmation,
        ThemedMessageDialogTone tone)
    {
        var dialog = new ThemedMessageDialog(
            message,
            title,
            confirmation,
            tone);
        var resolvedOwner = ResolveOwner(owner);
        if (resolvedOwner is not null)
            dialog.Owner = resolvedOwner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog;
    }

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner is { IsVisible: true })
            return owner;

        var activeWindow = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsVisible);
        if (activeWindow is not null)
            return activeWindow;

        return Application.Current.MainWindow is { IsVisible: true } main
            ? main
            : null;
    }

    private void ApplyTone(ThemedMessageDialogTone tone)
    {
        var (iconKey, brushKey) = tone switch
        {
            ThemedMessageDialogTone.Success
                => ("Long.Icon.Check", "Long.Brush.State.Success"),
            ThemedMessageDialogTone.Warning
                => ("Long.Icon.Warning", "Long.Brush.State.Warning"),
            ThemedMessageDialogTone.Danger
                => ("Long.Icon.Warning", "Long.Brush.State.Danger"),
            _ => ("Long.Icon.Plugin", "Long.Brush.Accent.Primary"),
        };
        ToneIcon.Data = (Geometry)FindResource(iconKey);
        ToneIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
        ToneBadge.SetResourceReference(
            BackgroundProperty,
            tone == ThemedMessageDialogTone.Info
                ? "Long.Brush.Accent.Soft"
                : "Long.Brush.Background.Raised");
        if (tone == ThemedMessageDialogTone.Danger)
        {
            PrimaryButton.SetResourceReference(
                StyleProperty,
                "LongButton.Danger");
        }
    }

    private static Color GetColor(Brush brush)
        => brush is SolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException(
                "Themed message dialog requires solid semantic brushes.");

    private void Header_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Cancel();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Cancel();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Cancel();
        e.Handled = true;
    }

    private void Cancel()
    {
        _confirmed = false;
        DialogResult = false;
        Close();
    }
}
