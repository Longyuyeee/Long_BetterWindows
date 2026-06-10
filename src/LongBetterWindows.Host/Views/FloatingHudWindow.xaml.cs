using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Views
{
    public partial class FloatingHudWindow : Window
    {
        private Action<string>? _onSave;
        private bool _dirty;
        private bool _isClosing;

        public FloatingHudWindow()
        {
            InitializeComponent();
        }

        public static FloatingHudWindow ShowAt(
            double x, double y,
            string? existingNote,
            Action<string> onSave)
        {
            var window = new FloatingHudWindow
            {
                Left = x,
                Top = y,
                _onSave = onSave,
            };

            if (!string.IsNullOrEmpty(existingNote))
            {
                window.NoteTextBox.Text = existingNote;
            }

            window.Show();
            window.NoteTextBox.Focus();
            window.NoteTextBox.CaretIndex = window.NoteTextBox.Text.Length;

            return window;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Window_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _dirty = false;
                    Close();
                    break;
                case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                    SaveAndClose();
                    break;
            }
        }

        private void NoteTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
        }

        private void NoteTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _dirty = true;
            HintText.Text = _dirty ? "已修改 · Ctrl+Enter 保存" : "";
        }

        public static void ShowToast(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var workArea = SystemParameters.WorkArea;
                int x = (int)(workArea.Right - 340);
                int y = (int)(workArea.Bottom - 80);

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Left = x,
                    Top = y,
                };

                var border = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0xE0, 0x32, 0x32, 0x32)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 10, 16, 10),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = message,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 13,
                    },
                };

                window.Content = border;
                window.Show();

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                window.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2),
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (_, _) => window.Close();
                    window.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                timer.Start();
            });
        }

        private void SaveAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            if (_dirty && _onSave != null)
            {
                var text = NoteTextBox.Text.Trim();
                _onSave(text);
            }

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
