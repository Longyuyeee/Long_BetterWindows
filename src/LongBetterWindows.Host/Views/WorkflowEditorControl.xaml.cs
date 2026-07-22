using System.IO;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkflowEditorControl : UserControl
    {
        private readonly PluginRegistry _plugins = HostProvider.Instance.PluginStore;
        private readonly CommandWorkflowRepository _repository;
        private readonly CommandWorkflowEditorSession _session;
        private bool _rendering = true;
        private bool _subscribed;

        public WorkflowEditorControl()
        {
            InitializeComponent();
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LongBetterWindows",
                "Workflows");
            _repository = new CommandWorkflowRepository(root, "local-managed");
            _session = new CommandWorkflowEditorSession(_plugins, _repository);
            FailureModeCombo.ItemsSource = FailureOptions;
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            _rendering = false;
        }

        private static IReadOnlyList<EnumOption<WorkflowFailureMode>> FailureOptions { get; } =
        [
            new(WorkflowFailureMode.Stop, "失败时停止"),
            new(WorkflowFailureMode.Compensate, "失败时回滚"),
        ];

        private static IReadOnlyList<EnumOption<WorkflowStepEffect>> EffectOptions { get; } =
        [
            new(WorkflowStepEffect.ReadOnly, "只读"),
            new(WorkflowStepEffect.Mutating, "会修改"),
        ];

        private async void WorkflowEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
            {
                _plugins.PluginsChanged += PluginsChanged;
                _subscribed = true;
            }
            await RefreshListAsync();
            RefreshCommandOptions();
        }

        private void WorkflowEditorControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed) return;
            _plugins.PluginsChanged -= PluginsChanged;
            _subscribed = false;
        }

        private void PluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(PluginsChanged);
                return;
            }
            RefreshCommandOptions();
            _session.RefreshPreflight();
            RenderEditor();
        }

        private async Task RefreshListAsync(string? selectWorkflowId = null)
        {
            var result = await _repository.ListManagedAsync();
            if (!result.IsSuccess)
            {
                SetListStatus(result.Error ?? "无法读取本机工作流", isError: true);
                return;
            }
            var items = result.Workflows.Select(WorkflowListItem.From).ToList();
            _rendering = true;
            WorkflowList.ItemsSource = items;
            CompactWorkflowCombo.ItemsSource = items;
            WorkflowCountText.Text = $"{items.Count} 项";
            SetListStatus(
                result.Issues.Count == 0 ? "本机托管 · 原子保存" : $"{result.Issues.Count} 个文件未能载入",
                result.Issues.Count > 0);
            if (selectWorkflowId is not null)
            {
                WorkflowList.SelectedItem = items.FirstOrDefault(item => string.Equals(
                    item.Id,
                    selectWorkflowId,
                    StringComparison.OrdinalIgnoreCase));
                CompactWorkflowCombo.SelectedValue = selectWorkflowId;
            }
            _rendering = false;
        }

        private void RefreshCommandOptions()
        {
            var options = _session.AvailableCommands
                .Select(CommandOption.From)
                .ToList();
            AddCommandCombo.ItemsSource = options;
            if (AddCommandCombo.SelectedIndex < 0 && options.Count > 0)
                AddCommandCombo.SelectedIndex = 0;
        }

        private async void WorkflowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering || WorkflowList.SelectedItem is not WorkflowListItem item) return;
            await _session.LoadAsync(item.Id);
            RenderEditor();
        }

        private async void CompactWorkflowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering || CompactWorkflowCombo.SelectedItem is not WorkflowListItem item) return;
            await _session.LoadAsync(item.Id);
            RenderEditor();
        }

        private void NewWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var suffix = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            _session.StartNew($"workflow.{suffix}", "新组合动作");
            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            RenderEditor();
            WorkflowNameBox.Focus();
            WorkflowNameBox.SelectAll();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshListAsync(_session.State.Draft?.Id);
            _session.RefreshPreflight();
            RefreshCommandOptions();
            RenderEditor();
        }

        private void Identity_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_rendering || _session.State.Draft is null) return;
            _session.UpdateIdentity(WorkflowIdBox.Text, WorkflowNameBox.Text);
            RenderStatus();
        }

        private void FailureMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering || FailureModeCombo.SelectedValue is not WorkflowFailureMode mode) return;
            _session.SetFailureMode(mode);
            RenderEditor();
        }

        private void AddStep_Click(object sender, RoutedEventArgs e)
        {
            if (AddCommandCombo.SelectedValue is not string commandKey) return;
            _session.AddStep(commandKey);
            RenderEditor();
        }

        private void StepCommand_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateStep(sender);

        private void StepEffect_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateStep(sender);

        private void StepCompensation_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateStep(sender);

        private void UpdateStep(object sender)
        {
            if (_rendering || sender is not FrameworkElement { DataContext: StepEditorItem item }) return;
            _session.UpdateStep(
                item.Id,
                item.Effect,
                item.CommandKey,
                string.IsNullOrWhiteSpace(item.CompensationKey) ? null : item.CompensationKey);
            RenderEditor();
        }

        private void MoveStepUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: StepEditorItem item } && _session.MoveStep(item.Id, -1))
                RenderEditor();
        }

        private void MoveStepDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: StepEditorItem item } && _session.MoveStep(item.Id, 1))
                RenderEditor();
        }

        private void RemoveStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: StepEditorItem item } && _session.RemoveStep(item.Id))
                RenderEditor();
        }

        private async void SaveWorkflow_Click(object sender, RoutedEventArgs e)
        {
            SaveWorkflowButton.IsEnabled = false;
            var result = await _session.SaveAsync(SensitiveInputCheckBox.IsChecked == true);
            RenderEditor();
            if (result.IsSuccess)
                await RefreshListAsync(_session.State.Draft?.Id);
        }

        private async void DeleteWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var draft = _session.State.Draft;
            if (draft is null || _session.State.ExistingDefinitionSha256 is null) return;
            var answer = MessageBox.Show(
                $"确定删除“{draft.Name}”吗？此操作不会执行工作流，但无法撤销。",
                "删除组合动作",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            var result = await _session.DeleteCurrentAsync();
            if (!result.IsSuccess)
            {
                RenderStatus();
                return;
            }
            RenderEditor();
            await RefreshListAsync();
        }

        private void RenderEditor()
        {
            _rendering = true;
            try
            {
                var state = _session.State;
                var draft = state.Draft;
                EmptyEditor.Visibility = draft is null ? Visibility.Visible : Visibility.Collapsed;
                EditorBody.Visibility = draft is null ? Visibility.Collapsed : Visibility.Visible;
                if (draft is null) return;

                EditorHeading.Text = draft.Name;
                WorkflowIdBox.Text = draft.Id;
                WorkflowIdBox.IsEnabled = state.ExistingDefinitionSha256 is null;
                WorkflowNameBox.Text = draft.Name;
                FailureModeCombo.SelectedValue = draft.FailureMode;
                var commands = _session.AvailableCommands.Select(CommandOption.From).ToList();
                var compensationOptions = new[] { new CommandOption(string.Empty, "不设置") }
                    .Concat(commands)
                    .ToList();
                StepsList.ItemsSource = draft.Steps.Select((step, index) => new StepEditorItem
                {
                    Id = step.Id,
                    Position = (index + 1).ToString("00"),
                    CommandKey = step.Command?.CommandKey ?? string.Empty,
                    Effect = step.Effect,
                    CompensationKey = step.Compensation?.CommandKey ?? string.Empty,
                    CanMoveUp = index > 0,
                    CanMoveDown = index < draft.Steps.Count - 1,
                    CommandOptions = commands,
                    CompensationOptions = compensationOptions,
                    EffectOptions = EffectOptions,
                }).ToList();
                EmptyStepsText.Visibility = draft.Steps.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SensitiveInputCheckBox.Visibility = CommandWorkflowDocumentCodec.ContainsSensitiveInputs(draft)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                DeleteWorkflowButton.IsEnabled = state.ExistingDefinitionSha256 is not null;
                RenderStatus();
            }
            finally
            {
                _rendering = false;
            }
        }

        private void RenderStatus()
        {
            var state = _session.State;
            EditorDirtyText.Text = state.ExistingDefinitionSha256 is null
                ? "尚未保存"
                : state.IsDirty ? "有未保存的更改" : "已保存到本机";
            SaveWorkflowButton.IsEnabled = state.CanSave && state.IsDirty;
            if (state.Preflight?.IsValid == true)
            {
                PreflightTitle.Text = "预检通过";
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Success");
                PreflightDetail.Text = state.Preflight.Permissions.Count == 0
                    ? "不需要插件能力授权"
                    : $"执行前将复核 {state.Preflight.Permissions.Count} 个插件及其能力";
            }
            else
            {
                PreflightTitle.Text = "需要修正";
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Danger");
                PreflightDetail.Text = state.Error
                    ?? string.Join(Environment.NewLine, state.Preflight?.Issues ?? Array.Empty<string>());
            }
        }

        private void SetListStatus(string text, bool isError)
        {
            ListStatusText.Text = text;
            ListStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
                isError ? "Long.Brush.State.Danger" : "Long.Brush.Text.Muted");
        }

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 700;
            WorkflowListColumn.Width = new GridLength(compact ? 0 : 232);
            WorkflowDividerColumn.Width = new GridLength(compact ? 0 : 1);
            WorkflowGapColumn.Width = new GridLength(compact ? 0 : 20);
            CompactWorkflowBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        private sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;
        private sealed record CommandOption(string Key, string Display)
        {
            public static CommandOption From(CommandDescriptor descriptor)
                => new(descriptor.Key, $"{descriptor.Command.Title} · {descriptor.PluginName}");
        }
        private sealed record WorkflowListItem(string Id, string Name, string Detail)
        {
            public static WorkflowListItem From(ManagedCommandWorkflowSummary summary)
                => new(
                    summary.Id,
                    summary.Name,
                    $"{summary.StepCount} 步 · {(summary.FailureMode == WorkflowFailureMode.Compensate ? "回滚" : "停止")}");
        }
        private sealed class StepEditorItem
        {
            public required string Id { get; init; }
            public required string Position { get; init; }
            public required string CommandKey { get; set; }
            public WorkflowStepEffect Effect { get; set; }
            public required string CompensationKey { get; set; }
            public bool IsMutating => Effect == WorkflowStepEffect.Mutating;
            public bool CanMoveUp { get; init; }
            public bool CanMoveDown { get; init; }
            public required IReadOnlyList<CommandOption> CommandOptions { get; init; }
            public required IReadOnlyList<CommandOption> CompensationOptions { get; init; }
            public required IReadOnlyList<EnumOption<WorkflowStepEffect>> EffectOptions { get; init; }
        }
    }
}
