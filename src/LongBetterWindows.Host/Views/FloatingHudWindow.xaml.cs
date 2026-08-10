using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Views
{
    public sealed record FloatingHudLocalization(
        string Title,
        string InputAutomationName,
        string EmptyHint,
        string ModifiedHint)
    {
        public static FloatingHudLocalization Default { get; } = new(
            "备注",
            "文件夹备注内容",
            "输入备注内容...",
            "已修改 · Ctrl+Enter 保存");
    }

    public partial class FloatingHudWindow : Window
    {
        private Func<string, Task>? _onSave;
        private bool _dirty;
        private bool _isClosing;
        private System.Threading.CancellationTokenSource? _closeCts;
        private FloatingHudLocalization _localization =
            FloatingHudLocalization.Default;

        public FloatingHudWindow()
        {
            InitializeComponent();
        }

        public static FloatingHudWindow ShowAt(
            double x, double y,
            string? existingNote,
            string? folderPath,
            Func<string, Task> onSave,
            FloatingHudLocalization? localization = null)
        {
            var window = new FloatingHudWindow
            {
                Left = x,
                Top = y,
                _onSave = onSave,
            };
            window.ApplyLocalization(
                localization ?? FloatingHudLocalization.Default);

            if (!string.IsNullOrEmpty(folderPath))
            {
                window.FolderLabel.Text = Path.GetFileName(folderPath);
            }

            if (!string.IsNullOrEmpty(existingNote))
            {
                window.NoteTextBox.Text = existingNote;
                window._dirty = false;
                window.HintText.Text = window._localization.EmptyHint;
            }

            window.Show();
            window.NoteTextBox.Focus();
            window.NoteTextBox.CaretIndex = window.NoteTextBox.Text.Length;

            return window;
        }

        public void ApplyLocalization(FloatingHudLocalization localization)
        {
            _localization = localization;
            TitleLabel.Text = localization.Title;
            AutomationProperties.SetName(
                NoteTextBox,
                localization.InputAutomationName);
            HintText.Text = _dirty
                ? localization.ModifiedHint
                : localization.EmptyHint;
        }

        public static void ShowToast(string message)
        {
            ToastWindow.Show(message);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Helpers.AnimationHelper.FadeIn(this, durationMs: 350);
            Helpers.AnimationHelper.ScaleBounce(this, from: 0.85, to: 1.0, durationMs: 400);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 失去焦点后 3 秒自动保存关闭，给用户时间切换回来
            _closeCts?.Cancel();
            _closeCts = new System.Threading.CancellationTokenSource();
            var token = _closeCts.Token;
            Task.Delay(3000, token).ContinueWith(completed =>
            {
                if (!token.IsCancellationRequested)
                {
                    var operation = Dispatcher.InvokeAsync(SaveAndCloseAsync);
                    _ = operation.Task.Unwrap();
                }
            });
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // 用户切回来了，取消自动关闭
            _closeCts?.Cancel();
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _dirty = false;
                    Close();
                    break;
                case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control:
                    _closeCts?.Cancel();
                    await SaveAndCloseAsync();
                    break;
            }
        }

        private void NoteTextBox_TextChanged(
            object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _dirty = true;
            HintText.Text = _localization.ModifiedHint;
        }

        private async Task SaveAndCloseAsync()
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                if (_dirty && _onSave != null)
                {
                    await _onSave(NoteTextBox.Text);
                }

                Helpers.AnimationHelper.FadeOut(this, durationMs: 150);
            }
            catch (Exception exception)
            {
                _isClosing = false;
                ShowToast(exception.Message);
                NoteTextBox.Focus();
            }
        }
    }
}
