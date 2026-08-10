using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;

namespace SamplePlugin;

public sealed record SampleSettingsLocalization(
    string AudienceLabel,
    string SaveAction,
    string Hint,
    string SavedStatus,
    string FailedStatusFormat);

public sealed class SampleSettingsControl : UserControl
{
    private readonly Func<string, Task<HostApiResponse>> _save;
    private readonly TextBlock _title;
    private readonly TextBlock _label;
    private readonly TextBox _audience;
    private readonly Button _saveButton;
    private readonly TextBlock _hint;
    private readonly TextBlock _status;
    private SampleSettingsLocalization _localization;

    public SampleSettingsControl(
        string pluginName,
        string audience,
        Func<string, Task<HostApiResponse>> save,
        SampleSettingsLocalization localization)
    {
        _save = save;
        _localization = localization;
        SetResourceReference(ForegroundProperty, "Long.Brush.Text.Primary");

        var panel = new StackPanel();
        _title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        };
        _title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Primary");

        _label = new TextBlock { FontSize = 12 };
        _label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Secondary");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 8),
        };
        _audience = new TextBox
        {
            Text = audience,
            MaxLength = 64,
            Width = 240,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _audience.SetResourceReference(StyleProperty, "LongTextBox");
        AutomationProperties.SetAutomationId(
            _audience,
            "Long.SampleSettings.Audience");

        _saveButton = new Button { Margin = new Thickness(8, 0, 0, 0) };
        _saveButton.SetResourceReference(StyleProperty, "LongButton.Primary");
        AutomationProperties.SetAutomationId(
            _saveButton,
            "Long.SampleSettings.Save");
        _saveButton.Click += SaveAsync;

        row.Children.Add(_audience);
        row.Children.Add(_saveButton);

        _hint = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _hint.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Muted");

        _status = new TextBlock { FontSize = 11 };
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Muted");
        AutomationProperties.SetAutomationId(
            _status,
            "Long.SampleSettings.Status");

        panel.Children.Add(_title);
        panel.Children.Add(_label);
        panel.Children.Add(row);
        panel.Children.Add(_hint);
        panel.Children.Add(_status);
        Content = panel;
        ApplyLocalization(pluginName, localization);
    }

    public void ApplyLocalization(
        string pluginName,
        SampleSettingsLocalization localization)
    {
        _localization = localization;
        _title.Text = pluginName;
        _label.Text = localization.AudienceLabel;
        _saveButton.Content = localization.SaveAction;
        _hint.Text = localization.Hint;
        AutomationProperties.SetName(_audience, localization.AudienceLabel);
        AutomationProperties.SetName(_saveButton, localization.SaveAction);
    }

    private async void SaveAsync(object sender, RoutedEventArgs args)
    {
        _saveButton.IsEnabled = false;
        try
        {
            var result = await _save(_audience.Text);
            if (result.IsSuccess)
            {
                _status.Text = _localization.SavedStatus;
                _status.SetResourceReference(
                    TextBlock.ForegroundProperty,
                    "Long.Brush.State.Success");
            }
            else
            {
                RenderFailure(result.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            RenderFailure(exception.Message);
        }
        finally
        {
            _saveButton.IsEnabled = true;
            AutomationProperties.SetItemStatus(this, _status.Text);
        }
    }

    private void RenderFailure(string? detail)
    {
        _status.Text = string.Format(
            _localization.FailedStatusFormat,
            detail ?? string.Empty);
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.State.Danger");
    }
}
