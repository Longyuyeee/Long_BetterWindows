using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class DeveloperPageControl : UserControl, IDisposable
    {
        private bool _disposed;

        public DeveloperPageControl()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
            HostProvider.Instance.PluginStore.PluginsChanged += OnPluginsChanged;
            ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
            RefreshAboutInfo();
            RefreshDocLinks();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SizeChanged -= OnSizeChanged;
            HostProvider.Instance.PluginStore.PluginsChanged -= OnPluginsChanged;
            ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyResponsiveLayout(e.NewSize.Width);

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 860;
            DeveloperGapColumn.Width = new GridLength(compact ? 0 : 12);
            DeveloperSecondaryColumn.Width = compact
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            Grid.SetRow(DesignCard, compact ? 1 : 0);
            Grid.SetColumn(DesignCard, compact ? 0 : 2);
            Grid.SetRow(DocsCard, compact ? 2 : 1);
            Grid.SetColumn(DocsCard, 0);
            Grid.SetRow(AboutCard, compact ? 3 : 1);
            Grid.SetColumn(AboutCard, compact ? 0 : 2);
        }

        private void OnPluginsChanged()
        {
            if (_disposed) return;
            _ = Dispatcher.BeginInvoke(RefreshAboutInfo);
        }

        private void OnLanguageChanged(string language)
        {
            if (_disposed) return;
            _ = Dispatcher.BeginInvoke(() =>
            {
                RefreshAboutInfo();
                RefreshDocLinks();
            });
        }

        private void RefreshAboutInfo()
        {
            if (_disposed) return;
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            AboutVersion.Text = string.Format(
                I18n("developer.about.version"),
                App.ProductVersion);
            AboutStats.Text = string.Format(
                I18n("developer.about.stats"),
                ManifestReader.KnownCapabilities.Count,
                plugins.Count,
                3);
        }

        private void RefreshDocLinks()
        {
            DocLinksPanel.Children.Clear();
            var docsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..", "docs"));
            if (!Directory.Exists(docsDir))
                docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");

            if (!Directory.Exists(docsDir))
            {
                AddStatus(I18n("developer.docs.unavailable"));
                return;
            }

            var docFiles = Directory.GetFiles(docsDir, "*.md")
                .OrderBy(path => path)
                .ToList();
            if (docFiles.Count == 0)
            {
                AddStatus(I18n("developer.docs.empty"));
                return;
            }

            for (var index = 0; index < docFiles.Count; index++)
            {
                var file = docFiles[index];
                var title = Path.GetFileNameWithoutExtension(file);
                var link = new Button
                {
                    Content = title,
                    Tag = file,
                    ToolTip = Path.GetFileName(file),
                };
                link.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    "DeveloperDocumentButton");
                AutomationProperties.SetAutomationId(
                    link,
                    $"Long.Developer.Document.{index + 1}");
                AutomationProperties.SetName(link, title);
                link.Click += (_, _) =>
                {
                    var path = (string)link.Tag;
                    DocViewer.ShowDoc(
                        Window.GetWindow(this)!,
                        Path.GetFileNameWithoutExtension(path),
                        File.ReadAllText(path));
                };
                DocLinksPanel.Children.Add(link);
            }
        }

        private void AddStatus(string text)
            => DocLinksPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });

        private void DevTools_Click(object sender, RoutedEventArgs e)
            => PluginDevTools.Open(Window.GetWindow(this)!);

        private void DesignSystemPreview_Click(object sender, RoutedEventArgs e)
        {
            var preview = new DesignSystemPreview { Owner = Window.GetWindow(this) };
            preview.Show();
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
