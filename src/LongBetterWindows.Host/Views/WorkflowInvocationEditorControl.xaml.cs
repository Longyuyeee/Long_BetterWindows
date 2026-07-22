using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using Microsoft.Win32;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkflowInvocationEditorControl : UserControl
    {
        public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
            nameof(Editor),
            typeof(WorkflowInvocationEditorModel),
            typeof(WorkflowInvocationEditorControl),
            new PropertyMetadata(null, EditorChanged));

        private bool _rendering;

        public WorkflowInvocationEditorControl()
        {
            InitializeComponent();
        }

        public WorkflowInvocationEditorModel? Editor
        {
            get => (WorkflowInvocationEditorModel?)GetValue(EditorProperty);
            set => SetValue(EditorProperty, value);
        }

        public event EventHandler? InvocationChanged;

        private static void EditorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not WorkflowInvocationEditorControl control) return;
            control._rendering = true;
            control.DataContext = e.NewValue;
            control._rendering = false;
        }

        private void InputType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not ComboBox { SelectedValue: AcceptedInputType inputType }) return;
            Editor.InputType = inputType;
            RaiseInvocationChanged();
        }

        private void Text_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering || Editor is null || sender is not TextBox textBox) return;
            Editor.Text = textBox.Text;
            RaiseInvocationChanged();
        }

        private void PickPaths_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null) return;
            IReadOnlyList<string>? paths;
            if (Editor.InputType == AcceptedInputType.Folder)
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "选择命令文件夹",
                    Multiselect = false,
                };
                paths = dialog.ShowDialog() == true ? [dialog.FolderName] : null;
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择命令文件",
                    CheckFileExists = true,
                    Multiselect = Editor.InputType is AcceptedInputType.Files
                        or AcceptedInputType.ExplorerSelection,
                };
                paths = dialog.ShowDialog() == true ? dialog.FileNames : null;
            }
            if (paths is null) return;
            Editor.Paths = paths;
            RaiseInvocationChanged();
        }

        private void ClearPaths_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null) return;
            Editor.Paths = Array.Empty<string>();
            RaiseInvocationChanged();
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null) return;
            var dialog = new OpenFileDialog
            {
                Title = "选择 PNG 图片",
                Filter = "PNG 图片 (*.png)|*.png",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var file = new FileInfo(dialog.FileName);
                if (file.Length > CommandWorkflowDocumentCodec.MaximumImageBytes)
                    throw new InvalidOperationException("PNG 图片不能超过 2 MB。");
                var bytes = File.ReadAllBytes(dialog.FileName);
                if (!IsPng(bytes)) throw new InvalidOperationException("选择的文件不是有效的 PNG 图片。");
                Editor.ImagePng = bytes;
                RaiseInvocationChanged();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                MessageBox.Show(ex.Message, "命令图片", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearImage_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null) return;
            Editor.ImagePng = null;
            RaiseInvocationChanged();
        }

        private void RaiseInvocationChanged()
            => InvocationChanged?.Invoke(this, EventArgs.Empty);

        private static bool IsPng(byte[] bytes)
            => bytes.Length >= 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4e
                && bytes[3] == 0x47
                && bytes[4] == 0x0d
                && bytes[5] == 0x0a
                && bytes[6] == 0x1a
                && bytes[7] == 0x0a;
    }

    public sealed class WorkflowInvocationEditorModel : INotifyPropertyChanged
    {
        private AcceptedInputType _inputType;
        private string _text = string.Empty;
        private IReadOnlyList<string> _paths = Array.Empty<string>();
        private byte[]? _imagePng;

        public required string StepId { get; init; }
        public WorkflowCommandRole Role { get; init; }
        public required string RoleLabel { get; init; }
        public required IReadOnlyList<WorkflowInputTypeOption> InputOptions { get; init; }
        public required IReadOnlyDictionary<string, string> Arguments { get; init; }

        public AcceptedInputType InputType
        {
            get => _inputType;
            set
            {
                if (_inputType == value) return;
                _inputType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowText));
                OnPropertyChanged(nameof(ShowPaths));
                OnPropertyChanged(nameof(ShowImage));
            }
        }

        public string Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        public IReadOnlyList<string> Paths
        {
            get => _paths;
            set
            {
                _paths = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPaths));
            }
        }

        public byte[]? ImagePng
        {
            get => _imagePng;
            set
            {
                _imagePng = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(ImageSummary));
            }
        }

        public bool ShowText => InputType is not (AcceptedInputType.None or AcceptedInputType.Image);
        public bool ShowPaths => InputType is AcceptedInputType.File
            or AcceptedInputType.Files
            or AcceptedInputType.Folder
            or AcceptedInputType.ExplorerSelection;
        public bool ShowImage => InputType == AcceptedInputType.Image;
        public bool HasPaths => Paths.Count > 0;
        public bool HasImage => ImagePng is { Length: > 0 };
        public string ImageSummary => HasImage ? $"已载入 {ImagePng!.Length:N0} 字节" : "尚未选择图片";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed record WorkflowInputTypeOption(AcceptedInputType Value, string Label);
}
