using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Services;
using LongBetterWindows.Host.Tools;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        private readonly FolderNoteTool _folderNoteTool = new();
        private bool _columnEnabled;

        public ToolCenterControl()
        {
            InitializeComponent();
            UpdateUI();
            RefreshColumnStatus();
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

        private async void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnButton.IsEnabled = false;
            ColumnStatusText.Text = "处理中...";

            if (_columnEnabled)
            {
                var result = await ServicesInitializer.ColumnInjection
                    .DisableCommentColumnAsync();

                if (result.IsSuccess)
                {
                    _columnEnabled = false;
                    ColumnStatusText.Text = "备注列已移除";
                }
                else
                {
                    ColumnStatusText.Text = "移除失败";
                }
            }
            else
            {
                var result = await ServicesInitializer.ColumnInjection
                    .EnableCommentColumnAsync();

                if (result.IsSuccess)
                {
                    _columnEnabled = true;
                    ColumnStatusText.Text = "备注列已启用 · Explorer 已刷新";
                    ColumnStatusText.Foreground = new SolidColorBrush(
                        Color.FromRgb(0x34, 0xC7, 0x59));
                }
                else
                {
                    ColumnStatusText.Text = "注入失败";
                }
            }

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";
            ColumnButton.IsEnabled = true;
        }

        private void RefreshColumnStatus()
        {
            _columnEnabled = ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";

            if (_columnEnabled)
            {
                ColumnStatusText.Text = "已启用";
                ColumnStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x34, 0xC7, 0x59));
            }
            else
            {
                ColumnStatusText.Text = "未启用";
                ColumnStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x80, 0x80, 0x80));
            }
        }

        private void UpdateUI()
        {
            if (_folderNoteTool.IsEnabled)
            {
                ToggleButton.Content = "禁用";
                StatusText.Text = "已启用 · 热键 Alt+M";
                StatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x34, 0xC7, 0x59));
            }
            else
            {
                ToggleButton.Content = "启用";
                StatusText.Text = "未启用";
                StatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(0x80, 0x80, 0x80));
            }
        }
    }
}
