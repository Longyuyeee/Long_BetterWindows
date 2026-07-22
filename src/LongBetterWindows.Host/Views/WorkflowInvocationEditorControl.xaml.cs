using System.ComponentModel;
using System.Collections.ObjectModel;
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

        private void AddArgument_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null || !Editor.AddArgument()) return;
            RaiseInvocationChanged();
        }

        private void RemoveArgument_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null
                || sender is not Button { Tag: WorkflowArgumentEditorItem argument }
                || !Editor.RemoveArgument(argument)) return;
            RaiseInvocationChanged();
        }

        private void ArgumentKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not TextBox { DataContext: WorkflowArgumentEditorItem argument } textBox) return;
            argument.Key = textBox.Text;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void ArgumentValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not TextBox { DataContext: WorkflowArgumentEditorItem argument } textBox) return;
            argument.Value = textBox.Text;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void AddBinding_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null || !Editor.BindingEditor.AddBinding()) return;
            RaiseInvocationChanged();
        }

        private void RemoveBinding_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null
                || sender is not Button { Tag: WorkflowBindingEditorItem binding }
                || !Editor.BindingEditor.RemoveBinding(binding)) return;
            RaiseInvocationChanged();
        }

        private void BindingOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || sender is not ComboBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    SelectedItem: WorkflowBindingOutputOption output,
                }) return;
            binding.Output = output;
            RaiseInvocationChanged();
        }

        private void BindingTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || sender is not ComboBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    SelectedValue: WorkflowBindingTarget target,
                }) return;
            binding.Target = target;
            RaiseInvocationChanged();
        }

        private void BindingArgumentKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || sender is not TextBox { DataContext: WorkflowBindingEditorItem binding } textBox) return;
            binding.ArgumentKey = textBox.Text;
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
        private string? _argumentError;

        public required string StepId { get; init; }
        public WorkflowCommandRole Role { get; init; }
        public required string RoleLabel { get; init; }
        public required IReadOnlyList<WorkflowInputTypeOption> InputOptions { get; init; }
        public ObservableCollection<WorkflowArgumentEditorItem> Arguments { get; } = new();
        public WorkflowBindingEditorModel BindingEditor { get; set; } = new(
            Array.Empty<WorkflowBindingOutputOption>(),
            AcceptedInputType.None);

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
                BindingEditor.SetInputType(value);
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
        public string ArgumentSummary => Arguments.Count == 0 ? "高级参数" : $"高级参数 · {Arguments.Count}";
        public string? ArgumentError
        {
            get => _argumentError;
            private set
            {
                if (!SetField(ref _argumentError, value)) return;
                OnPropertyChanged(nameof(HasArgumentError));
            }
        }
        public bool HasArgumentError => ArgumentError is not null;
        public bool CanAddArgument => Arguments.Count < 64;

        public void LoadArguments(IReadOnlyDictionary<string, string> arguments)
        {
            Arguments.Clear();
            foreach (var argument in arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
                Arguments.Add(new WorkflowArgumentEditorItem(argument.Key, argument.Value));
            RefreshArgumentValidation();
            NotifyArgumentCollectionChanged();
        }

        public bool AddArgument()
        {
            if (!CanAddArgument) return false;
            var used = Arguments.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var key = "argument";
            for (var number = 2; used.Contains(key); number++) key = $"argument-{number}";
            Arguments.Add(new WorkflowArgumentEditorItem(key, string.Empty));
            RefreshArgumentValidation();
            NotifyArgumentCollectionChanged();
            return true;
        }

        public bool RemoveArgument(WorkflowArgumentEditorItem argument)
        {
            var removed = Arguments.Remove(argument);
            if (!removed) return false;
            RefreshArgumentValidation();
            NotifyArgumentCollectionChanged();
            return true;
        }

        public bool TryBuildArguments(out IReadOnlyDictionary<string, string> arguments)
        {
            RefreshArgumentValidation();
            if (ArgumentError is not null)
            {
                arguments = new Dictionary<string, string>();
                return false;
            }
            arguments = Arguments.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            return true;
        }

        public void RefreshArgumentValidation()
        {
            if (Arguments.Any(item => string.IsNullOrWhiteSpace(item.Key)))
                ArgumentError = "参数键不能为空。";
            else if (Arguments.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
                ArgumentError = "参数键不能重复。";
            else
                ArgumentError = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void NotifyArgumentCollectionChanged()
        {
            OnPropertyChanged(nameof(ArgumentSummary));
            OnPropertyChanged(nameof(CanAddArgument));
        }
    }

    public sealed record WorkflowInputTypeOption(AcceptedInputType Value, string Label);

    public sealed class WorkflowArgumentEditorItem
    {
        public WorkflowArgumentEditorItem(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; set; }
        public string Value { get; set; }
    }
}
