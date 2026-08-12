using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Helpers;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkflowInvocationEditorControl : UserControl
    {
        public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
            nameof(Editor),
            typeof(WorkflowInvocationEditorModel),
            typeof(WorkflowInvocationEditorControl),
            new PropertyMetadata(null));

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

        private void InputType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not ComboBox
                {
                    SelectedValue: AcceptedInputType inputType,
                    IsKeyboardFocusWithin: true,
                }) return;
            Editor.InputType = inputType;
            RaiseInvocationChanged();
        }

        private void Text_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not TextBox { IsKeyboardFocusWithin: true } textBox) return;
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
                    Title = I18n("workflow.invocation.dialog.chooseFolder"),
                    Multiselect = false,
                };
                paths = dialog.ShowDialog(DialogOwnerResolver.Resolve(this)) == true
                    ? [dialog.FolderName]
                    : null;
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = I18n("workflow.invocation.dialog.chooseFile"),
                    CheckFileExists = true,
                    Multiselect = Editor.InputType is AcceptedInputType.Files
                        or AcceptedInputType.ExplorerSelection,
                };
                paths = dialog.ShowDialog(DialogOwnerResolver.Resolve(this)) == true
                    ? dialog.FileNames
                    : null;
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
                Title = I18n("workflow.invocation.dialog.choosePng"),
                Filter = I18n("workflow.invocation.dialog.pngFilter"),
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog(DialogOwnerResolver.Resolve(this)) != true) return;
            try
            {
                var file = new FileInfo(dialog.FileName);
                if (file.Length > CommandWorkflowDocumentCodec.MaximumImageBytes)
                    throw new InvalidOperationException(
                        I18n("workflow.invocation.imageTooLarge"));
                var bytes = File.ReadAllBytes(dialog.FileName);
                if (!IsPng(bytes))
                    throw new InvalidOperationException(
                        I18n("workflow.invocation.imageInvalid"));
                Editor.ImagePng = bytes;
                RaiseInvocationChanged();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ThemedMessageDialog.ShowAlert(
                    Window.GetWindow(this),
                    ex.Message,
                    I18n("workflow.invocation.imageDialogTitle"),
                    ThemedMessageDialogTone.Warning);
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

        private void ApplyArgumentPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null || !Editor.ApplySelectedArgumentPreset()) return;
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
                || sender is not TextBox
                {
                    DataContext: WorkflowArgumentEditorItem argument,
                    IsKeyboardFocusWithin: true,
                } textBox) return;
            argument.Key = textBox.Text;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void ArgumentValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not TextBox
                {
                    DataContext: WorkflowArgumentEditorItem argument,
                    IsKeyboardFocusWithin: true,
                } textBox) return;
            argument.Value = textBox.Text;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void SchemaArgumentValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not TextBox
                {
                    DataContext: WorkflowSchemaArgumentEditorItem argument,
                } textBox
                || !textBox.IsKeyboardFocusWithin) return;
            argument.Value = textBox.Text;
            RaiseInvocationChanged();
        }

        private void SchemaSensitive_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox
                {
                    DataContext: WorkflowSchemaArgumentEditorItem argument,
                } passwordBox) return;
            _rendering = true;
            passwordBox.Password = argument.Value;
            _rendering = false;
        }

        private void SchemaSensitive_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not PasswordBox
                {
                    DataContext: WorkflowSchemaArgumentEditorItem argument,
                } passwordBox) return;
            argument.Value = passwordBox.Password;
            RaiseInvocationChanged();
        }

        private void SchemaBoolean_Changed(object sender, RoutedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not CheckBox
                {
                    DataContext: WorkflowSchemaArgumentEditorItem argument,
                } checkBox
                || !checkBox.IsKeyboardFocusWithin) return;
            argument.BooleanValue = checkBox.IsChecked;
            RaiseInvocationChanged();
        }

        private void SchemaEnum_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not ComboBox
                {
                    DataContext: WorkflowSchemaArgumentEditorItem argument,
                } comboBox
                || !comboBox.IsKeyboardFocusWithin) return;
            argument.SelectedEnumValue = comboBox.SelectedItem as string;
            RaiseInvocationChanged();
        }

        private void AddBinding_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null || !Editor.BindingEditor.AddBinding()) return;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void RemoveBinding_Click(object sender, RoutedEventArgs e)
        {
            if (Editor is null
                || sender is not Button { Tag: WorkflowBindingEditorItem binding }
                || !Editor.BindingEditor.RemoveBinding(binding)) return;
            Editor.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void BindingOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || sender is not ComboBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    SelectedItem: WorkflowBindingOutputOption output,
                    IsKeyboardFocusWithin: true,
                }) return;
            binding.Output = output;
            Editor?.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void BindingTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || sender is not ComboBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    SelectedValue: WorkflowBindingTarget target,
                    IsKeyboardFocusWithin: true,
                }) return;
            binding.Target = target;
            Editor?.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void BindingArgumentKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering
                || sender is not TextBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    IsKeyboardFocusWithin: true,
                } textBox) return;
            binding.ArgumentKey = textBox.Text;
            Editor?.RefreshArgumentValidation();
            RaiseInvocationChanged();
        }

        private void BindingArgumentKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering
                || Editor is null
                || sender is not ComboBox
                {
                    DataContext: WorkflowBindingEditorItem binding,
                    SelectedItem: string argumentKey,
                    IsKeyboardFocusWithin: true,
                }) return;
            binding.ArgumentKey = argumentKey;
            Editor.RefreshArgumentValidation();
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

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }

    public sealed class WorkflowInvocationEditorModel : INotifyPropertyChanged
    {
        private AcceptedInputType _inputType;
        private string _text = string.Empty;
        private IReadOnlyList<string> _paths = Array.Empty<string>();
        private byte[]? _imagePng;
        private string? _argumentError;
        private WorkflowArgumentPresetOption? _selectedArgumentPreset;
        public required string StepId { get; init; }
        public WorkflowCommandRole Role { get; init; }
        public required string RoleLabel { get; init; }
        public required IReadOnlyList<WorkflowInputTypeOption> InputOptions { get; init; }
        public ObservableCollection<WorkflowArgumentEditorItem> Arguments { get; } = new();
        public ObservableCollection<WorkflowSchemaArgumentEditorItem> SchemaArguments { get; } = new();
        public IReadOnlyList<PluginCommandArgumentDeclaration> ArgumentSchema { get; init; } =
            Array.Empty<PluginCommandArgumentDeclaration>();
        public IReadOnlyList<WorkflowArgumentPresetOption> ArgumentPresets { get; init; } =
            Array.Empty<WorkflowArgumentPresetOption>();
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
        public string ImageSummary => HasImage
            ? string.Format(I18n("workflow.invocation.imageLoaded"), ImagePng!.Length)
            : I18n("workflow.invocation.imageEmpty");
        public bool UsesArgumentSchema => ArgumentSchema.Count > 0;
        public bool UsesAdvancedArguments => !UsesArgumentSchema;
        public bool HasUnrecognizedArguments => UsesArgumentSchema && Arguments.Count > 0;
        public string ArgumentSummary
        {
            get
            {
                if (UsesArgumentSchema)
                {
                    var suffix = HasArgumentError
                        ? I18n("workflow.invocation.argumentsNeedsFixSuffix")
                        : string.Empty;
                    return string.Format(
                        I18n("workflow.invocation.schemaArgumentsSummary"),
                        ArgumentSchema.Count,
                        suffix);
                }
                return Arguments.Count == 0
                    ? I18n("workflow.invocation.advancedArguments")
                    : string.Format(
                        I18n("workflow.invocation.advancedArgumentsCount"),
                        Arguments.Count);
            }
        }
        public string? ArgumentError
        {
            get => _argumentError;
            private set
            {
                if (!SetField(ref _argumentError, value)) return;
                OnPropertyChanged(nameof(HasArgumentError));
                OnPropertyChanged(nameof(ArgumentSummary));
            }
        }
        public bool HasArgumentError => ArgumentError is not null;
        public bool HasArgumentPresets => ArgumentPresets.Count > 0;
        public bool CanAddArgument => UsesAdvancedArguments && Arguments.Count < 64;
        public WorkflowArgumentPresetOption? SelectedArgumentPreset
        {
            get => _selectedArgumentPreset;
            set
            {
                if (!SetField(ref _selectedArgumentPreset, value)) return;
                OnPropertyChanged(nameof(CanApplyArgumentPreset));
            }
        }
        public bool CanApplyArgumentPreset => SelectedArgumentPreset is not null;

        public void LoadArguments(IReadOnlyDictionary<string, string> arguments)
        {
            Arguments.Clear();
            SchemaArguments.Clear();
            if (UsesArgumentSchema)
            {
                var declaredKeys = ArgumentSchema
                    .Select(declaration => declaration.Key)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var declaration in ArgumentSchema)
                {
                    var hasValue = arguments.TryGetValue(declaration.Key, out var value);
                    if (!hasValue && declaration.DefaultValue is not null)
                    {
                        hasValue = true;
                        value = declaration.DefaultValue;
                    }
                    SchemaArguments.Add(new WorkflowSchemaArgumentEditorItem(
                        declaration,
                        hasValue,
                        value ?? string.Empty,
                        RefreshArgumentValidation));
                }
                foreach (var argument in arguments
                    .Where(item => !declaredKeys.Contains(item.Key))
                    .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    Arguments.Add(new WorkflowArgumentEditorItem(argument.Key, argument.Value));
                }
            }
            else
            {
                foreach (var argument in arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
                    Arguments.Add(new WorkflowArgumentEditorItem(argument.Key, argument.Value));
            }
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

        public bool ApplySelectedArgumentPreset()
        {
            var selected = SelectedArgumentPreset;
            if (selected is null) return false;
            var registered = ArgumentPresets.FirstOrDefault(preset => string.Equals(
                preset.Id,
                selected.Id,
                StringComparison.OrdinalIgnoreCase));
            if (registered is null) return false;
            LoadArguments(new Dictionary<string, string>(
                registered.Arguments,
                StringComparer.Ordinal));
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
            if (UsesAdvancedArguments)
            {
                arguments = Arguments.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal);
                return true;
            }

            var draft = BuildSchemaArgumentDraft();
            var validation = PluginCommandArgumentValidator.ValidateForWorkflowPreflight(
                ArgumentSchema,
                draft,
                DeferredArgumentKeys());
            if (!validation.IsSuccess)
            {
                arguments = new Dictionary<string, string>();
                return false;
            }
            arguments = validation.Arguments;
            return true;
        }

        public void RefreshArgumentValidation()
        {
            if (UsesArgumentSchema)
            {
                RefreshSchemaArgumentValidation();
            }
            else if (Arguments.Any(item => string.IsNullOrWhiteSpace(item.Key)))
                ArgumentError = I18n("workflow.invocation.error.keyRequired");
            else if (Arguments.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
                ArgumentError = I18n("workflow.invocation.error.keyDuplicate");
            else
                ArgumentError = null;
        }

        private void RefreshSchemaArgumentValidation()
        {
            var draft = BuildSchemaArgumentDraft();
            var validation = PluginCommandArgumentValidator.ValidateForWorkflowPreflight(
                ArgumentSchema,
                draft,
                DeferredArgumentKeys());
            foreach (var item in SchemaArguments)
            {
                var keyToken = $"'{item.Key}'";
                item.SetError(validation.Issues.FirstOrDefault(issue =>
                    issue.Contains(keyToken, StringComparison.Ordinal)));
            }
            ArgumentError = validation.IsSuccess
                ? null
                : string.Join(" ", validation.Issues);
        }

        private IReadOnlyDictionary<string, string> BuildSchemaArgumentDraft()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in SchemaArguments)
            {
                if (item.HasValue)
                    result[item.Key] = item.Value;
            }
            foreach (var item in Arguments)
                result[item.Key] = item.Value;
            return result;
        }

        private IEnumerable<string> DeferredArgumentKeys()
            => BindingEditor.Bindings
                .Where(binding => binding.Target == WorkflowBindingTarget.Argument
                    && !string.IsNullOrWhiteSpace(binding.ArgumentKey))
                .Select(binding => binding.ArgumentKey);

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
            OnPropertyChanged(nameof(HasUnrecognizedArguments));
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }

    public sealed record WorkflowInputTypeOption(AcceptedInputType Value, string Label);

    public sealed record WorkflowArgumentPresetOption(
        string Id,
        string Name,
        IReadOnlyDictionary<string, string> Arguments);

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

    public sealed class WorkflowSchemaArgumentEditorItem : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private string _value;
        private bool _hasValue;
        private bool? _booleanValue;
        private string? _selectedEnumValue;
        private string? _error;

        public WorkflowSchemaArgumentEditorItem(
            PluginCommandArgumentDeclaration declaration,
            bool hasValue,
            string value,
            Action changed)
        {
            Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
            _changed = changed ?? throw new ArgumentNullException(nameof(changed));
            _hasValue = hasValue;
            _value = value;
            if (declaration.Type == PluginCommandArgumentType.Boolean
                && hasValue
                && bool.TryParse(value, out var boolean))
            {
                _booleanValue = boolean;
            }
            if (declaration.Type == PluginCommandArgumentType.Enum && hasValue)
                _selectedEnumValue = declaration.EnumValues.Contains(value, StringComparer.Ordinal)
                    ? value
                    : null;
        }

        public PluginCommandArgumentDeclaration Declaration { get; }
        public string Key => Declaration.Key;
        public string Name => Declaration.Required ? $"{Declaration.Name} *" : Declaration.Name;
        public string Description => Declaration.Description;
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
        public bool IsSensitiveEditor => Declaration.Sensitive;
        public bool IsBooleanEditor => !Declaration.Sensitive
            && Declaration.Type == PluginCommandArgumentType.Boolean;
        public bool IsEnumEditor => !Declaration.Sensitive
            && Declaration.Type == PluginCommandArgumentType.Enum;
        public bool IsTextEditor => !Declaration.Sensitive
            && Declaration.Type is PluginCommandArgumentType.String
                or PluginCommandArgumentType.Integer
                or PluginCommandArgumentType.Number;
        public bool AllowsUnset => !Declaration.Required && Declaration.DefaultValue is null;
        public int InputMaxLength => Declaration.Type == PluginCommandArgumentType.String
            ? Declaration.MaxLength ?? 65536
            : 128;
        public IReadOnlyList<string> EnumValues => Declaration.EnumValues;
        public string AutomationName => Declaration.Sensitive
            ? string.Format(
                I18n("workflow.invocation.sensitiveAutomationName"),
                Declaration.Name)
            : Declaration.Name;
        public string ConstraintSummary => BuildConstraintSummary(Declaration);

        public bool HasValue => _hasValue;
        public string Value
        {
            get => _value;
            set
            {
                if (_value == value && _hasValue) return;
                _value = value;
                _hasValue = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasValue));
                _changed();
            }
        }
        public bool? BooleanValue
        {
            get => _booleanValue;
            set
            {
                if (_booleanValue == value && _hasValue == value.HasValue) return;
                _booleanValue = value;
                _hasValue = value.HasValue;
                _value = value.HasValue ? (value.Value ? "true" : "false") : string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(HasValue));
                _changed();
            }
        }
        public string? SelectedEnumValue
        {
            get => _selectedEnumValue;
            set
            {
                if (_selectedEnumValue == value && _hasValue == (value is not null)) return;
                _selectedEnumValue = value;
                _hasValue = value is not null;
                _value = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(HasValue));
                _changed();
            }
        }
        public string? Error
        {
            get => _error;
            private set
            {
                if (_error == value) return;
                _error = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
        public bool HasError => Error is not null;

        internal void SetError(string? error) => Error = error;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string BuildConstraintSummary(
            PluginCommandArgumentDeclaration declaration)
        {
            var parts = new List<string>
            {
                declaration.Type switch
                {
                    PluginCommandArgumentType.Integer => I18n("workflow.argumentType.integer"),
                    PluginCommandArgumentType.Number => I18n("workflow.argumentType.number"),
                    PluginCommandArgumentType.Boolean => I18n("workflow.argumentType.boolean"),
                    PluginCommandArgumentType.Enum => I18n("workflow.argumentType.enum"),
                    _ => I18n("workflow.argumentType.text"),
                },
            };
            if (declaration.Minimum.HasValue || declaration.Maximum.HasValue)
                parts.Add(string.Format(
                    I18n("workflow.constraint.range"),
                    declaration.Minimum?.ToString() ?? I18n("workflow.constraint.unlimited"),
                    declaration.Maximum?.ToString() ?? I18n("workflow.constraint.unlimited")));
            if (declaration.MinLength.HasValue || declaration.MaxLength.HasValue)
                parts.Add(string.Format(
                    I18n("workflow.constraint.length"),
                    declaration.MinLength?.ToString() ?? "0",
                    declaration.MaxLength?.ToString() ?? I18n("workflow.constraint.unlimited")));
            if (declaration.DefaultValue is not null && !declaration.Sensitive)
                parts.Add(string.Format(
                    I18n("workflow.constraint.default"),
                    declaration.DefaultValue));
            if (declaration.Sensitive)
                parts.Add(I18n("workflow.constraint.sensitive"));
            return string.Join(" · ", parts);
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
