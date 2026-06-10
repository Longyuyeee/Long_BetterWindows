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
