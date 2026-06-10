using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace QuickLaunchPlugin;

public partial class LaunchWindow : Window
{
    private readonly List<AppEntry> _allApps = new();
    private Action<string?>? _onSelect;

    public LaunchWindow()
    {
        InitializeComponent();
        LoadApps();
    }

    public static void Show(Action<string?> onSelect)
    {
        var workArea = SystemParameters.WorkArea;
        var window = new LaunchWindow
        {
            _onSelect = onSelect,
            Left = workArea.Left + (workArea.Width - 400) / 2,
            Top = workArea.Top + workArea.Height * 0.3,
        };

        window.Show();
        window.SearchBox.Focus();
    }

    private void LoadApps()
    {
        var paths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var startMenu in paths)
        {
            if (!Directory.Exists(startMenu)) continue;

            foreach (var lnk in Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    _allApps.Add(new AppEntry { Name = name, Path = lnk });
                }
                catch { }
            }
        }

        _allApps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            ResultsList.Visibility = Visibility.Collapsed;
            return;
        }

        var results = _allApps
            .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        ResultsList.ItemsSource = results;
        ResultsList.Visibility = results.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (results.Count > 0)
            ResultsList.SelectedIndex = 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ResultsList.Items.Count > 0)
                {
                    ResultsList.Focus();
                    if (ResultsList.SelectedIndex < 0)
                        ResultsList.SelectedIndex = 0;
                }
                e.Handled = true;
                break;

            case Key.Enter:
                if (ResultsList.SelectedItem is AppEntry entry)
                    SelectAndClose(entry.Path);
                e.Handled = true;
                break;

            case Key.Escape:
                _onSelect?.Invoke(null);
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is AppEntry entry)
            SelectAndClose(entry.Path);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !SearchBox.IsFocused)
        {
            _onSelect?.Invoke(null);
            Close();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _onSelect?.Invoke(null);
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void SelectAndClose(string path)
    {
        _onSelect?.Invoke(path);
        Close();
    }
}

public class AppEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
