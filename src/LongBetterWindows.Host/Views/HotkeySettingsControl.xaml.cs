using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    /// <summary>
    /// 共享热键设置控件——所有插件的设置面板中复用。
    /// 提供热键显示、修改、冲突检测的完整 UI。
    /// </summary>
    public partial class HotkeySettingsControl : UserControl
    {
        private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(0x00, 0x7A, 0xFF));

        private static Brush? TryFindBrush(string key)
            => Application.Current.TryFindResource(key) as Brush;

        private readonly IHotKeyService _hotKey;
        private readonly string _pluginId;
        private readonly string _pluginName;
        private readonly Action<string> _onHotkeyChanged;
        private string _currentHotkey;

        public HotkeySettingsControl(
            string pluginName,
            string pluginId,
            string currentHotkey,
            Action<string> onHotkeyChanged)
        {
            _pluginName = pluginName;
            _pluginId = pluginId;
            _currentHotkey = currentHotkey;
            _onHotkeyChanged = onHotkeyChanged;
            _hotKey = ServicesInitializer.HotKey;

            var panel = new StackPanel();

            var title = new TextBlock
            {
                Text = $"{pluginName}",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16),
            };
            panel.Children.Add(title);

            var label = new TextBlock
            {
                Text = "当前快捷键",
                FontSize = 12,
                Foreground = TryFindBrush("TextSecondaryBrush") ?? GrayBrush,
            };
            panel.Children.Add(label);

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
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var applyBtn = new Button
            {
                Content = "应用",
                Width = 56,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
                Background = TryFindBrush("AccentBlueBrush") ?? BlueBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };

            var statusText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
            };

            applyBtn.Click += async (_, _) =>
            {
                var newHotkey = hotkeyBox.Text.Trim();
                if (string.IsNullOrEmpty(newHotkey) || newHotkey == _currentHotkey)
                {
                    statusText.Text = "未修改";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
                    return;
                }

                // 检查冲突
                var conflictResult = await _hotKey.IsConflictAsync(newHotkey);
                if (conflictResult.IsSuccess && conflictResult.Data)
                {
                    var owner = _hotKey.GetOwner(newHotkey);
                    if (owner != null && owner != _pluginId)
                    {
                        statusText.Text = $"冲突: 已被「{owner}」占用";
                        statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                        return;
                    }
                }

                // 更换热键
                var changeResult = await _hotKey.ChangeHotkeyAsync(
                    _currentHotkey, newHotkey, _pluginId, () => { });

                if (changeResult.IsSuccess)
                {
                    _currentHotkey = newHotkey;
                    _onHotkeyChanged(newHotkey);
                    statusText.Text = "已更新";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
                }
                else
                {
                    statusText.Text = changeResult.ErrorMessage ?? "修改失败";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
                }
            };

            hotkeyRow.Children.Add(hotkeyBox);
            hotkeyRow.Children.Add(applyBtn);
            panel.Children.Add(hotkeyRow);
            panel.Children.Add(statusText);

            var hint = new TextBlock
            {
                Text = "格式: Ctrl+K  Alt+M  Win+N  Ctrl+Shift+Space  F6",
                FontSize = 11,
                Foreground = TryFindBrush("TextDimBrush") ?? GrayBrush,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(hint);

            Content = panel;
        }
    }
}
