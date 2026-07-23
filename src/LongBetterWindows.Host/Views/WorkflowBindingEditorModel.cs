using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public sealed record WorkflowBindingOutputOption(
        string SourceStepId,
        string OutputKey,
        PluginCommandOutputType Type,
        string Description,
        bool IsAvailable = true)
    {
        public string Display => string.IsNullOrWhiteSpace(Description)
            ? $"{SourceStepId} / {OutputKey}"
            : $"{SourceStepId} / {OutputKey} - {Description}";
    }

    public sealed record WorkflowBindingTargetOption(
        WorkflowBindingTarget Value,
        string Label);

    public sealed class WorkflowBindingEditorModel : INotifyPropertyChanged
    {
        private const int MaximumBindings = 64;
        private readonly IReadOnlyList<WorkflowBindingOutputOption> _availableOutputs;
        private readonly IReadOnlyList<string> _declaredArgumentKeys;
        private AcceptedInputType _inputType;
        private string? _error;

        public WorkflowBindingEditorModel(
            IReadOnlyList<WorkflowBindingOutputOption> availableOutputs,
            AcceptedInputType inputType,
            IReadOnlyList<string>? declaredArgumentKeys = null)
        {
            _availableOutputs = availableOutputs ?? throw new ArgumentNullException(nameof(availableOutputs));
            _inputType = inputType;
            _declaredArgumentKeys = (declaredArgumentKeys ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public ObservableCollection<WorkflowBindingEditorItem> Bindings { get; } = new();
        public string Summary => Bindings.Count == 0
            ? I18n("workflow.binding.summary")
            : string.Format(I18n("workflow.binding.summaryCount"), Bindings.Count);
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
        public bool CanAdd => Bindings.Count < MaximumBindings
            && _availableOutputs.Any(output =>
                TargetOptions(output.Type).Any(CanUseTarget));

        public void SetInputType(AcceptedInputType inputType)
        {
            if (_inputType == inputType) return;
            _inputType = inputType;
            foreach (var binding in Bindings) binding.RefreshTargetOptions();
            RefreshValidation();
            NotifyCollectionState();
        }

        public void LoadBindings(IReadOnlyList<WorkflowValueBinding>? bindings)
        {
            Bindings.Clear();
            foreach (var binding in bindings ?? Array.Empty<WorkflowValueBinding>())
            {
                var output = _availableOutputs.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceStepId, binding.SourceStepId, StringComparison.Ordinal)
                    && string.Equals(candidate.OutputKey, binding.OutputKey, StringComparison.Ordinal));
                if (output is null)
                {
                    output = new WorkflowBindingOutputOption(
                        binding.SourceStepId,
                        binding.OutputKey,
                        binding.Target == WorkflowBindingTarget.Path
                            ? PluginCommandOutputType.Path
                            : PluginCommandOutputType.Text,
                        I18n("workflow.binding.outputUnavailable"),
                        IsAvailable: false);
                }
                Bindings.Add(new WorkflowBindingEditorItem(
                    this,
                    output,
                    binding.Target,
                    binding.ArgumentKey ?? string.Empty));
            }
            RefreshValidation();
            NotifyCollectionState();
        }

        public bool AddBinding()
        {
            if (!CanAdd) return false;
            var output = _availableOutputs.First(candidate =>
                TargetOptions(candidate.Type).Any(CanUseTarget));
            var targets = TargetOptions(output.Type).Where(CanUseTarget).ToArray();
            var target = targets[0];
            Bindings.Add(new WorkflowBindingEditorItem(
                this,
                output,
                target.Value,
                target.Value == WorkflowBindingTarget.Argument ? NextArgumentKey() : string.Empty));
            RefreshValidation();
            NotifyCollectionState();
            return true;
        }

        public bool RemoveBinding(WorkflowBindingEditorItem binding)
        {
            var removed = Bindings.Remove(binding);
            if (!removed) return false;
            RefreshValidation();
            NotifyCollectionState();
            return true;
        }

        public bool TryBuildBindings(out IReadOnlyList<WorkflowValueBinding> bindings)
        {
            RefreshValidation();
            if (Error is not null)
            {
                bindings = Array.Empty<WorkflowValueBinding>();
                return false;
            }
            bindings = Bindings.Select(binding => new WorkflowValueBinding(
                binding.Output.SourceStepId,
                binding.Output.OutputKey,
                binding.Target,
                binding.Target == WorkflowBindingTarget.Argument ? binding.ArgumentKey : null))
                .ToArray();
            return true;
        }

        public void RefreshValidation()
        {
            if (Bindings.Any(binding => !binding.Output.IsAvailable))
                Error = I18n("workflow.binding.error.sourceUnavailable");
            else if (Bindings.Any(binding => !binding.TargetOptions.Any(option => option.Value == binding.Target)))
                Error = I18n("workflow.binding.error.incompatibleTarget");
            else if (Bindings.Count(binding => binding.Target == WorkflowBindingTarget.Text) > 1)
                Error = I18n("workflow.binding.error.textOnce");
            else if (Bindings.Any(binding => binding.Target == WorkflowBindingTarget.Argument
                && string.IsNullOrWhiteSpace(binding.ArgumentKey)))
                Error = I18n("workflow.binding.error.keyRequired");
            else if (Bindings.Any(binding => binding.Target == WorkflowBindingTarget.Argument
                && binding.ArgumentKey.Length > 128))
                Error = I18n("workflow.binding.error.keyTooLong");
            else if (Bindings.Where(binding => binding.Target == WorkflowBindingTarget.Argument)
                .GroupBy(binding => binding.ArgumentKey, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
                Error = I18n("workflow.binding.error.keyDuplicate");
            else if (_declaredArgumentKeys.Count > 0
                && Bindings.Any(binding => binding.Target == WorkflowBindingTarget.Argument
                    && !_declaredArgumentKeys.Contains(
                        binding.ArgumentKey,
                        StringComparer.Ordinal)))
                Error = I18n("workflow.binding.error.schemaRequired");
            else
                Error = null;
        }

        internal IReadOnlyList<WorkflowBindingOutputOption> OutputOptions(
            WorkflowBindingOutputOption selected)
        {
            var compatible = _availableOutputs
                .Where(output => TargetOptions(output.Type).Count > 0)
                .ToArray();
            return compatible.Contains(selected)
                ? compatible
                : new[] { selected }.Concat(compatible).ToArray();
        }

        internal IReadOnlyList<WorkflowBindingTargetOption> TargetOptions(
            PluginCommandOutputType outputType)
        {
            if (outputType == PluginCommandOutputType.Path)
            {
                return (_inputType is AcceptedInputType.File
                    or AcceptedInputType.Files
                    or AcceptedInputType.Folder
                    or AcceptedInputType.ExplorerSelection)
                    ? [new WorkflowBindingTargetOption(
                        WorkflowBindingTarget.Path,
                        I18n("workflow.binding.target.path"))]
                    : Array.Empty<WorkflowBindingTargetOption>();
            }
            var options = new List<WorkflowBindingTargetOption>();
            if (_inputType is not (AcceptedInputType.None or AcceptedInputType.Image))
                options.Add(new WorkflowBindingTargetOption(
                    WorkflowBindingTarget.Text,
                    I18n("workflow.binding.target.text")));
            options.Add(new WorkflowBindingTargetOption(
                WorkflowBindingTarget.Argument,
                I18n("workflow.binding.target.argument")));
            return options;
        }

        internal IReadOnlyList<string> ArgumentKeyOptions => _declaredArgumentKeys;

        internal void ItemChanged()
        {
            RefreshValidation();
            NotifyCollectionState();
        }

        private bool CanUseTarget(WorkflowBindingTargetOption option)
            => option.Value switch
            {
                WorkflowBindingTarget.Text =>
                    Bindings.All(binding => binding.Target != WorkflowBindingTarget.Text),
                WorkflowBindingTarget.Argument when _declaredArgumentKeys.Count > 0 =>
                    _declaredArgumentKeys.Any(key => Bindings.All(binding =>
                        binding.Target != WorkflowBindingTarget.Argument
                        || !string.Equals(binding.ArgumentKey, key, StringComparison.Ordinal))),
                _ => true,
            };

        public event PropertyChangedEventHandler? PropertyChanged;

        private string NextArgumentKey()
        {
            var used = Bindings.Where(binding => binding.Target == WorkflowBindingTarget.Argument)
                .Select(binding => binding.ArgumentKey)
                .ToHashSet(StringComparer.Ordinal);
            var declared = _declaredArgumentKeys.FirstOrDefault(key => !used.Contains(key));
            if (declared is not null) return declared;
            var key = "output";
            for (var number = 2; used.Contains(key); number++) key = $"output-{number}";
            return key;
        }

        private void NotifyCollectionState()
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CanAdd));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }

    public sealed class WorkflowBindingEditorItem : INotifyPropertyChanged
    {
        private readonly WorkflowBindingEditorModel _owner;
        private WorkflowBindingOutputOption _output;
        private WorkflowBindingTarget _target;
        private string _argumentKey;

        internal WorkflowBindingEditorItem(
            WorkflowBindingEditorModel owner,
            WorkflowBindingOutputOption output,
            WorkflowBindingTarget target,
            string argumentKey)
        {
            _owner = owner;
            _output = output;
            _target = target;
            _argumentKey = argumentKey;
        }

        public WorkflowBindingOutputOption Output
        {
            get => _output;
            set
            {
                if (_output == value) return;
                _output = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputOptions));
                RefreshTargetOptions(selectCompatibleTarget: true);
                _owner.ItemChanged();
            }
        }
        public IReadOnlyList<WorkflowBindingOutputOption> OutputOptions => _owner.OutputOptions(Output);
        public WorkflowBindingTarget Target
        {
            get => _target;
            set
            {
                if (_target == value) return;
                _target = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowArgumentKey));
                OnPropertyChanged(nameof(ShowArgumentKeyTextBox));
                OnPropertyChanged(nameof(ShowArgumentKeyOptions));
                _owner.ItemChanged();
            }
        }
        public IReadOnlyList<WorkflowBindingTargetOption> TargetOptions => _owner.TargetOptions(Output.Type);
        public IReadOnlyList<string> ArgumentKeyOptions => _owner.ArgumentKeyOptions;
        public string ArgumentKey
        {
            get => _argumentKey;
            set
            {
                if (_argumentKey == value) return;
                _argumentKey = value;
                OnPropertyChanged();
                _owner.ItemChanged();
            }
        }
        public bool ShowArgumentKey => Target == WorkflowBindingTarget.Argument;
        public bool ShowArgumentKeyOptions => ShowArgumentKey && ArgumentKeyOptions.Count > 0;
        public bool ShowArgumentKeyTextBox => ShowArgumentKey && ArgumentKeyOptions.Count == 0;

        internal void RefreshTargetOptions(bool selectCompatibleTarget = false)
        {
            OnPropertyChanged(nameof(TargetOptions));
            var options = TargetOptions;
            if (selectCompatibleTarget
                && options.Count > 0
                && options.All(option => option.Value != Target))
            {
                Target = options[0].Value;
            }
            OnPropertyChanged(nameof(ShowArgumentKey));
            OnPropertyChanged(nameof(ShowArgumentKeyTextBox));
            OnPropertyChanged(nameof(ShowArgumentKeyOptions));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
