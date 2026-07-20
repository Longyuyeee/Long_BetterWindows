using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginManagementControl : UserControl
    {
        public PluginManagementControl()
        {
            InitializeComponent();
            HostProvider.Instance.PluginStore.PluginsChanged += OnPluginsChanged;
        }

        public void Refresh()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            PluginsHeader.Text = $"已安装插件 ({plugins.Count})";

            var filter = PluginSearchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? plugins
                : plugins.Where(plugin =>
                    plugin.Manifest.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || plugin.Manifest.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            var items = filtered.Select(PluginCardItem.Create).ToArray();

            PluginsPanel.ItemsSource = items;
            PluginsPanel.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = plugins.Count == 0
                ? "暂无已安装插件"
                : $"没有找到匹配“{filter}”的插件";
            EmptyStateText.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Refresh);
                return;
            }
            Refresh();
        }

        private void PluginSearch_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            PluginSearchBox.Clear();
            Refresh();
        }

        private void OpenPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: PluginCardItem { Entry.Instance: IHasMainUI mainUi } })
                mainUi.ShowMainUI();
        }

        private async void PluginToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: PluginCardItem item } button) return;
            button.IsEnabled = false;
            try
            {
                var registry = HostProvider.Instance.PluginStore;
                var entry = registry.Get(item.Entry.Id);
                if (entry is null) return;

                if (entry.State == PluginState.Running)
                    await registry.StopPluginAsync(entry.Id);
                else
                    await registry.StartPluginAsync(entry.Id);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin toggle error: {exception.Message}");
            }
            finally
            {
                button.IsEnabled = true;
                Refresh();
            }
        }

        private void PluginSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: PluginCardItem { Entry: var entry } }
                || entry.Instance is not IHasSettingsUI settingsUi) return;

            try
            {
                var content = settingsUi.CreateSettingsUI();
                if (content is null) return;

                new PluginWindowHost($"{entry.Manifest.Name} · 设置", content, entry.Manifest.Window)
                {
                    Owner = Window.GetWindow(this),
                    Width = 520,
                    Height = 420,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                }.ShowDialog();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Settings UI error: {exception.Message}");
            }
        }

        private void CapabilityDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: PluginCardItem { Entry: var entry } }) return;

            var panel = new CapabilityDetailPanel();
            panel.LoadCapabilities(entry.Id, entry.Manifest.Name, entry.Manifest.Capabilities);
            new PluginWindowHost(
                $"{entry.Manifest.Name} · 权限详情",
                panel,
                new PluginWindowPreference
                {
                    Mode = PluginWindowMode.Standard,
                    PreferredWidth = 720,
                    PreferredHeight = 640,
                    MinWidth = 560,
                    MinHeight = 460,
                })
            {
                Owner = Window.GetWindow(this),
                ShowInTaskbar = false,
            }.ShowDialog();
        }

        private sealed class PluginCardItem
        {
            private PluginCardItem(PluginEntry entry)
            {
                Entry = entry;
                Name = entry.Manifest.Name;
                RuntimeLabel = entry.Manifest.Runtime switch
                {
                    "webview" => "Web",
                    "csharp-script" => "Script",
                    _ => "DLL",
                };
                IsRunning = entry.State == PluginState.Running;
                StatusText = $"{(IsRunning ? "运行中" : "已停止")} · v{entry.Manifest.Version}";
                ToggleText = IsRunning ? "禁用" : "启用";
                Hotkey = PluginRegistry.GetPluginHotkey(entry) ?? string.Empty;
                HasHotkey = !string.IsNullOrEmpty(Hotkey);
                VisibleCapabilities = entry.Manifest.Capabilities.Take(3).ToArray();
                HasAdditionalCapabilities = entry.Manifest.Capabilities.Count > VisibleCapabilities.Count;
                AdditionalCapabilityText = $"+{entry.Manifest.Capabilities.Count - VisibleCapabilities.Count}";
                CanOpen = entry.Instance is IHasMainUI;
                CanOpenSettings = entry.Instance is IHasSettingsUI;
            }

            public PluginEntry Entry { get; }
            public string Name { get; }
            public string RuntimeLabel { get; }
            public bool IsRunning { get; }
            public string StatusText { get; }
            public string ToggleText { get; }
            public string Hotkey { get; }
            public bool HasHotkey { get; }
            public IReadOnlyList<string> VisibleCapabilities { get; }
            public bool HasAdditionalCapabilities { get; }
            public string AdditionalCapabilityText { get; }
            public bool CanOpen { get; }
            public bool CanOpenSettings { get; }

            public static PluginCardItem Create(PluginEntry entry) => new(entry);
        }
    }
}
