using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;

namespace MacroPlugin;

internal sealed record MacroLoopIntervalLocalization(
    string Title,
    string Description,
    string Unit,
    string ApplyAction,
    string UnchangedStatus,
    string UpdatedStatus,
    string InvalidStatusFormat,
    string FailedStatusFormat,
    string Hint);

internal sealed class MacroLoopIntervalSettingsControl : UserControl
{
    private readonly Func<int, Task<HostApiResponse>> _apply;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBox _interval;
    private readonly TextBlock _unit;
    private readonly Button _applyButton;
    private readonly TextBlock _status;
    private readonly TextBlock _hint;
    private int _currentMilliseconds;
    private MacroLoopIntervalLocalization _localization;
    private MacroLoopIntervalStatus _statusKind;
    private string? _statusDetail;

    internal MacroLoopIntervalSettingsControl(
        int currentMilliseconds,
        Func<int, Task<HostApiResponse>> apply,
        MacroLoopIntervalLocalization localization)
    {
        _currentMilliseconds = currentMilliseconds;
        _apply = apply;
        _localization = localization;
        SetResourceReference(ForegroundProperty, "Long.Brush.Text.Primary");

        var panel = new StackPanel();
        _title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        _title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Primary");

        _description = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        _description.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Secondary");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 6),
        };
        _interval = new TextBox
        {
            Width = 160,
            MaxLength = 5,
            Text = currentMilliseconds.ToString(CultureInfo.InvariantCulture),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _interval.SetResourceReference(StyleProperty, "LongTextBox");
        AutomationProperties.SetAutomationId(
            _interval,
            "Long.MacroSettings.LoopInterval");

        _unit = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _unit.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Secondary");

        _applyButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
        };
        _applyButton.SetResourceReference(StyleProperty, "LongButton.Primary");
        AutomationProperties.SetAutomationId(
            _applyButton,
            "Long.MacroSettings.ApplyLoopInterval");
        _applyButton.Click += ApplyAsync;

        row.Children.Add(_interval);
        row.Children.Add(_unit);
        row.Children.Add(_applyButton);

        _status = new TextBlock { FontSize = 11 };
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Muted");
        AutomationProperties.SetAutomationId(
            _status,
            "Long.MacroSettings.LoopIntervalStatus");

        _hint = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        _hint.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Long.Brush.Text.Muted");

        panel.Children.Add(_title);
        panel.Children.Add(_description);
        panel.Children.Add(row);
        panel.Children.Add(_status);
        panel.Children.Add(_hint);
        Content = panel;
        ApplyLocalization(localization);
    }

    internal void ApplyLocalization(MacroLoopIntervalLocalization localization)
    {
        _localization = localization;
        _title.Text = localization.Title;
        _description.Text = localization.Description;
        _unit.Text = localization.Unit;
        _applyButton.Content = localization.ApplyAction;
        _hint.Text = localization.Hint;
        AutomationProperties.SetName(_interval, localization.Title);
        AutomationProperties.SetName(_applyButton, localization.ApplyAction);
        RenderStatus();
    }

    private async void ApplyAsync(object sender, RoutedEventArgs args)
    {
        if (!MacroLoopIntervalPolicy.TryParse(
                _interval.Text.Trim(),
                out var milliseconds))
        {
            RenderInvalid();
            return;
        }

        if (milliseconds == _currentMilliseconds)
        {
            SetStatus(MacroLoopIntervalStatus.Unchanged);
            return;
        }

        _applyButton.IsEnabled = false;
        try
        {
            var result = await _apply(milliseconds);
            if (result.IsSuccess)
            {
                _currentMilliseconds = milliseconds;
                _interval.Text = milliseconds.ToString(
                    CultureInfo.InvariantCulture);
                SetStatus(MacroLoopIntervalStatus.Updated);
            }
            else
            {
                SetStatus(
                    MacroLoopIntervalStatus.Failed,
                    result.ErrorMessage ?? string.Empty);
            }
        }
        catch (Exception exception)
        {
            SetStatus(
                MacroLoopIntervalStatus.Failed,
                exception.Message);
        }
        finally
        {
            _applyButton.IsEnabled = true;
        }
    }

    private void RenderInvalid()
    {
        SetStatus(MacroLoopIntervalStatus.Invalid);
    }

    private void SetStatus(
        MacroLoopIntervalStatus status,
        string? detail = null)
    {
        _statusKind = status;
        _statusDetail = detail;
        RenderStatus();
    }

    private void RenderStatus()
    {
        _status.Text = _statusKind switch
        {
            MacroLoopIntervalStatus.Unchanged =>
                _localization.UnchangedStatus,
            MacroLoopIntervalStatus.Updated =>
                _localization.UpdatedStatus,
            MacroLoopIntervalStatus.Invalid => string.Format(
                _localization.InvalidStatusFormat,
                MacroLoopIntervalPolicy.MinimumMilliseconds,
                MacroLoopIntervalPolicy.MaximumMilliseconds),
            MacroLoopIntervalStatus.Failed => string.Format(
                _localization.FailedStatusFormat,
                _statusDetail ?? string.Empty),
            _ => string.Empty,
        };
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            _statusKind switch
            {
                MacroLoopIntervalStatus.Updated =>
                    "Long.Brush.State.Success",
                MacroLoopIntervalStatus.Invalid or
                    MacroLoopIntervalStatus.Failed =>
                    "Long.Brush.State.Danger",
                _ => "Long.Brush.Text.Muted",
            });
        AutomationProperties.SetItemStatus(this, _status.Text);
    }

    private enum MacroLoopIntervalStatus
    {
        None,
        Unchanged,
        Updated,
        Invalid,
        Failed,
    }
}
