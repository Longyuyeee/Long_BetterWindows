using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Tools;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        private readonly FolderNoteTool _folderNoteTool = new();

        public ToolCenterControl()
        {
            InitializeComponent();
            UpdateUI();
        }

        private async void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton.IsEnabled = false;

            if (_folderNoteTool.IsEnabled)
            {
                await _folderNoteTool.DisableAsync();
            }
            else
            {
                await _folderNoteTool.EnableAsync();
            }

            UpdateUI();
            ToggleButton.IsEnabled = true;
        }

        private void UpdateUI()
        {
            if (_folderNoteTool.IsEnabled)
            {
                ToggleButton.Content = "禁用";
                StatusText.Text = "已启用 · 热键 Alt+M";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x34, 0xC7, 0x59));
            }
            else
            {
                ToggleButton.Content = "启用";
                StatusText.Text = "未启用";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
            }
        }
    }
}
