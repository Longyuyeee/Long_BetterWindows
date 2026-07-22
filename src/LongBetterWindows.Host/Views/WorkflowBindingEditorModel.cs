using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

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
        private AcceptedInputType _inputType;
        private string? _error;

        public WorkflowBindingEditorModel(
            IReadOnlyList<WorkflowBindingOutputOption> availableOutputs,
            AcceptedInputType inputType)
        {
            _availableOutputs = availableOutputs ?? throw new ArgumentNullException(nameof(availableOutputs));
            _inputType = inputType;
        }

        public ObservableCollection<WorkflowBindingEditorItem> Bindings { get; } = new();
        public string Summary => Bindings.Count == 0 ? "步骤输出绑定" : $"步骤输出绑定 · {Bindings.Count}";
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
            && _availableOutputs.Any(output => TargetOptions(output.Type).Count > 0);

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
                        "声明已不可用",
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
            var output = _availableOutputs.First(candidate => TargetOptions(candidate.Type).Count > 0);
            var targets = TargetOptions(output.Type);
            var target = targets.FirstOrDefault(option =>
                    option.Value != WorkflowBindingTarget.Text
                    || Bindings.All(binding => binding.Target != WorkflowBindingTarget.Text))
                ?? targets[0];
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
                Error = "绑定来源已不再由可用命令声明。";
            else if (Bindings.Any(binding => !binding.TargetOptions.Any(option => option.Value == binding.Target)))
                Error = "绑定目标与当前输入类型不兼容。";
            else if (Bindings.Count(binding => binding.Target == WorkflowBindingTarget.Text) > 1)
                Error = "文本目标只能绑定一次。";
            else if (Bindings.Any(binding => binding.Target == WorkflowBindingTarget.Argument
                && string.IsNullOrWhiteSpace(binding.ArgumentKey)))
                Error = "参数绑定键不能为空。";
            else if (Bindings.Any(binding => binding.Target == WorkflowBindingTarget.Argument
                && binding.ArgumentKey.Length > 128))
                Error = "参数绑定键不能超过 128 个字符。";
            else if (Bindings.Where(binding => binding.Target == WorkflowBindingTarget.Argument)
                .GroupBy(binding => binding.ArgumentKey, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
                Error = "参数绑定键不能重复。";
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
                    ? [new WorkflowBindingTargetOption(WorkflowBindingTarget.Path, "路径输入")]
                    : Array.Empty<WorkflowBindingTargetOption>();
            }
            var options = new List<WorkflowBindingTargetOption>();
            if (_inputType is not (AcceptedInputType.None or AcceptedInputType.Image))
                options.Add(new WorkflowBindingTargetOption(WorkflowBindingTarget.Text, "文本输入"));
            options.Add(new WorkflowBindingTargetOption(WorkflowBindingTarget.Argument, "命令参数"));
            return options;
        }

        internal void ItemChanged()
        {
            RefreshValidation();
            NotifyCollectionState();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private string NextArgumentKey()
        {
            var used = Bindings.Where(binding => binding.Target == WorkflowBindingTarget.Argument)
                .Select(binding => binding.ArgumentKey)
                .ToHashSet(StringComparer.Ordinal);
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
                _owner.ItemChanged();
            }
        }
        public IReadOnlyList<WorkflowBindingTargetOption> TargetOptions => _owner.TargetOptions(Output.Type);
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
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
