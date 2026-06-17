using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Views
{
    public partial class FloatingHudWindow : Window
    {
        private Action<string>? _onSave;
        private bool _dirty;
        private bool _isClosing;
        private System.Threading.CancellationTokenSource? _closeCts;

        public FloatingHudWindow()
        {
            InitializeComponent();
        }

        public static FloatingHudWindow ShowAt(
            double x, double y,
            string? existingNote,
            string? folderPath,
            Action<string> onSave)
        {
            var window = new FloatingHudWindow
            {
                Left = x,
                Top = y,
                _onSave = onSave,
            };

            if (!string.IsNullOrEmpty(folderPath))
            {
                window.FolderLabel.Text = Path.GetFileName(folderPath);
            }

            if (!string.IsNullOrEmpty(existingNote))
            {
                window.NoteTextBox.Text = existingNote;
            }

            window.Show();
            window.NoteTextBox.Focus();
            window.NoteTextBox.CaretIndex = window.NoteTextBox.Text.Length;

            return window;
        }

        public static void ShowToast(string message)
        {
            ToastWindow.Show(message);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 根据主题适配 HUD 背景色
            var isDark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()
                == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            HudBorder.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(0xE8, 0x2D, 0x2D, 0x30))
                : new SolidColorBrush(Color.FromArgb(0xE8, 0xF0, 0xF8, 0xF5));

            // 淡入 + 弹性缩放 (复用 AnimationHelper)
            Helpers.AnimationHelper.FadeIn(this, durationMs: 350);
            Helpers.AnimationHelper.ScaleBounce(this, from: 0.85, to: 1.0, durationMs: 400);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 失去焦点后 3 秒自动保存关闭，给用户时间切换回来
            _closeCts?.Cancel();
            _closeCts = new System.Threading.CancellationTokenSource();
            var token = _closeCts.Token;
            Task.Delay(3000, token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    Dispatcher.Invoke(() => SaveAndClose());
            });
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // 用户切回来了，取消自动关闭
            _closeCts?.Cancel();
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
                    _closeCts?.Cancel();
                    SaveAndClose();
                    break;
            }
        }

        private void NoteTextBox_TextChanged(
            object sender, System.Windows.Controls.TextChangedEventArgs e)
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

            Helpers.AnimationHelper.FadeOut(this, durationMs: 150);
        }
    }
}
