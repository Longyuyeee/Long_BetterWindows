using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public sealed record HotkeySettingsLocalization(
        string CurrentHotkeyLabel,
        string ApplyAction,
        string UnchangedStatus,
        string ConflictStatusFormat,
        string UpdatedStatus,
        string ChangeFailedStatusFormat,
        string FormatHint)
    {
        public static HotkeySettingsLocalization Chinese { get; } = new(
            "当前快捷键",
            "应用",
            "未修改",
            "冲突: 已被「{0}」占用",
            "已更新",
            "修改失败: {0}",
            "格式: Ctrl+K  Alt+M  Win+N  Ctrl+Shift+Space  F6");
    }

    /// <summary>
    /// 共享热键设置控件——所有插件的设置面板中复用。
    /// 提供热键显示、修改、冲突检测的完整 UI。
    /// </summary>
    public partial class HotkeySettingsControl : UserControl
    {
        private readonly IHotKeyService _hotKey;
        private readonly string _pluginId;
        private readonly Action<string> _onHotkeyChanged;
        private readonly Action _hotkeyCallback;
        private readonly TextBlock _title;
        private readonly TextBlock _label;
        private readonly Button _applyButton;
        private readonly TextBlock _statusText;
        private readonly TextBlock _hint;
        private string _currentHotkey;
        private HotkeySettingsLocalization _localization;
        private HotkeySettingsStatus _status;
        private string? _statusDetail;

        public HotkeySettingsControl(
            string pluginName,
            string pluginId,
            string currentHotkey,
            Action<string> onHotkeyChanged,
            HotkeySettingsLocalization? localization = null,
            Action? hotkeyCallback = null)
        {
            _pluginId = pluginId;
            _currentHotkey = currentHotkey;
            _onHotkeyChanged = onHotkeyChanged;
            _hotkeyCallback = hotkeyCallback ?? (() => { });
            _hotKey = ServicesInitializer.HotKey;
            _localization = localization ?? HotkeySettingsLocalization.Chinese;
            SetResourceReference(
                ForegroundProperty,
                "Long.Brush.Text.Primary");

            var panel = new StackPanel();

            _title = new TextBlock
            {
                Text = pluginName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16),
            };
            _title.SetResourceReference(
                TextBlock.ForegroundProperty,
                "Long.Brush.Text.Primary");
            panel.Children.Add(_title);

            _label = new TextBlock
            {
                FontSize = 12,
            };
            _label.SetResourceReference(
                TextBlock.ForegroundProperty,
                "Long.Brush.Text.Secondary");
            panel.Children.Add(_label);

            var hotkeyRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 8),
            };

            var hotkeyBox = new TextBox
            {
                Text = _currentHotkey,
                FontSize = 16,
                FontWeight = FontWeights.Medium,
                Width = 180,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            hotkeyBox.SetResourceReference(
                FrameworkElement.StyleProperty,
                "LongTextBox");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                hotkeyBox,
                "Long.HotkeySettings.Input");

            _applyButton = new Button
            {
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
            };
            _applyButton.SetResourceReference(
                FrameworkElement.StyleProperty,
                "LongButton.Primary");
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                _applyButton,
                "Long.HotkeySettings.Apply");

            _statusText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
            };

            _applyButton.Click += async (_, _) =>
            {
                var newHotkey = hotkeyBox.Text.Trim();
                if (string.IsNullOrEmpty(newHotkey) || newHotkey == _currentHotkey)
                {
                    SetStatus(HotkeySettingsStatus.Unchanged);
                    return;
                }

                // 检查冲突
                var conflictResult = await _hotKey.IsConflictAsync(
                    newHotkey,
                    _currentHotkey);
                if (conflictResult.IsSuccess && conflictResult.Data)
                {
                    var owner = _hotKey.GetOwner(newHotkey);
                    SetStatus(
                        HotkeySettingsStatus.Conflict,
                        owner ?? newHotkey);
                    return;
                }

                // 更换热键
                var changeResult = await _hotKey.ChangeHotkeyAsync(
                    _currentHotkey, newHotkey, _pluginId, _hotkeyCallback);

                if (changeResult.IsSuccess)
                {
                    _currentHotkey = newHotkey;
                    _onHotkeyChanged(newHotkey);
                    SetStatus(HotkeySettingsStatus.Updated);
                }
                else
                {
                    SetStatus(
                        HotkeySettingsStatus.Failed,
                        changeResult.ErrorMessage ?? string.Empty);
                }
            };

            hotkeyRow.Children.Add(hotkeyBox);
            hotkeyRow.Children.Add(_applyButton);
            panel.Children.Add(hotkeyRow);
            panel.Children.Add(_statusText);

            _hint = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            _hint.SetResourceReference(
                TextBlock.ForegroundProperty,
                "Long.Brush.Text.Muted");
            panel.Children.Add(_hint);

            Content = panel;
            ApplyLocalization(pluginName, _localization);
        }

        public void ApplyLocalization(
            string pluginName,
            HotkeySettingsLocalization localization)
        {
            _localization = localization;
            _title.Text = pluginName;
            _label.Text = localization.CurrentHotkeyLabel;
            _applyButton.Content = localization.ApplyAction;
            _hint.Text = localization.FormatHint;
            RenderStatus();
        }

        private void SetStatus(HotkeySettingsStatus status, string? detail = null)
        {
            _status = status;
            _statusDetail = detail;
            RenderStatus();
        }

        private void RenderStatus()
        {
            _statusText.Text = _status switch
            {
                HotkeySettingsStatus.Unchanged => _localization.UnchangedStatus,
                HotkeySettingsStatus.Conflict => string.Format(
                    _localization.ConflictStatusFormat,
                    _statusDetail),
                HotkeySettingsStatus.Updated => _localization.UpdatedStatus,
                HotkeySettingsStatus.Failed => string.Format(
                    _localization.ChangeFailedStatusFormat,
                    _statusDetail),
                _ => string.Empty,
            };
            _statusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                _status switch
            {
                HotkeySettingsStatus.Conflict or HotkeySettingsStatus.Failed
                    => "Long.Brush.State.Danger",
                HotkeySettingsStatus.Updated
                    => "Long.Brush.State.Success",
                _ => "Long.Brush.Text.Muted",
            });
        }

        private enum HotkeySettingsStatus
        {
            None,
            Unchanged,
            Conflict,
            Updated,
            Failed,
        }
    }
}
