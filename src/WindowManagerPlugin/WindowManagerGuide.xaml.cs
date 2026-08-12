using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace WindowManagerPlugin;

public sealed record WindowManagerGuideLocalization(
    string Title,
    string Description,
    string CloseAutomationName,
    string Common,
    string Topmost,
    string Maximize,
    string Layouts,
    string Left,
    string Right,
    string Bottom,
    string TopLeft,
    string TopRight,
    string BottomLeft,
    string BottomRight,
    string ThirdLeft,
    string ThirdRight,
    string Hint);

public partial class WindowManagerGuide : Window
{
    public WindowManagerGuide(WindowManagerGuideLocalization localization)
    {
        InitializeComponent();
        ApplyLocalization(localization);
    }

    public void ApplyLocalization(WindowManagerGuideLocalization localization)
    {
        Title = localization.Title;
        TitleText.Text = localization.Title;
        DescriptionText.Text = localization.Description;
        AutomationProperties.SetName(
            CloseButton,
            localization.CloseAutomationName);
        CommonTitleText.Text = localization.Common;
        TopmostText.Text = localization.Topmost;
        MaximizeText.Text = localization.Maximize;
        LayoutsTitleText.Text = localization.Layouts;
        LeftText.Text = localization.Left;
        RightText.Text = localization.Right;
        BottomText.Text = localization.Bottom;
        TopLeftText.Text = localization.TopLeft;
        TopRightText.Text = localization.TopRight;
        BottomLeftText.Text = localization.BottomLeft;
        BottomRightText.Text = localization.BottomRight;
        ThirdLeftText.Text = localization.ThirdLeft;
        ThirdRightText.Text = localization.ThirdRight;
        HintText.Text = localization.Hint;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
}
