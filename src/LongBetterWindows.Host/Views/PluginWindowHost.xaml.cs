using System.Windows;
using System.Windows.Input;
using LongBetterWindows.Host.Contracts;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class PluginWindowHost : Window
    {
        private Window? _returnTarget;

        public PluginWindowHost(
            string pluginId,
            string title,
            FrameworkElement content,
            PluginWindowPreference? preference = null)
        {
            InitializeComponent();
            Title = title;
            PluginTitle.Text = title;
            PluginContent.Content = content;
            Icon = PluginTaskbarIdentity.CreateIcon(pluginId, title);
            SourceInitialized += (_, _) =>
            {
                if (!PluginTaskbarIdentity.Apply(this, pluginId))
                    Log.Warning(
                        "Could not apply detached taskbar identity for plugin {PluginId}",
                        pluginId);
            };
            ApplyWindowPreference(preference);
        }

        internal void SetReturnTarget(Window? target) => _returnTarget = target;

        private void ApplyWindowPreference(PluginWindowPreference? preference)
        {
            if (preference == null) return;

            (Width, Height) = preference.Mode switch
            {
                PluginWindowMode.Compact => (560, 400),
                PluginWindowMode.Wide => (1040, 680),
                PluginWindowMode.Document => (980, 760),
                PluginWindowMode.Overlay => (720, 520),
                PluginWindowMode.FullScreen => (SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height),
                _ => (720, 560),
            };

            if (preference.PreferredWidth is > 0) Width = preference.PreferredWidth.Value;
            if (preference.PreferredHeight is > 0) Height = preference.PreferredHeight.Value;
            if (preference.MinWidth is > 0) MinWidth = preference.MinWidth.Value;
            if (preference.MinHeight is > 0) MinHeight = preference.MinHeight.Value;

            if (preference.Mode == PluginWindowMode.FullScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = SystemParameters.WorkArea.Left;
                Top = SystemParameters.WorkArea.Top;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Back_Click(object sender, RoutedEventArgs e) => ReturnToOwner();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            ReturnToOwner();
            e.Handled = true;
        }

        private void ReturnToOwner()
        {
            var owner = _returnTarget ?? Owner;
            Close();
            if (owner is null) return;
            if (!owner.IsVisible) owner.Show();
            if (owner.WindowState == WindowState.Minimized)
                owner.WindowState = WindowState.Normal;
            owner.Activate();
        }

        public void DetachContent() => PluginContent.Content = null;
    }
}
