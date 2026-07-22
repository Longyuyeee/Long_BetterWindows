using System.IO;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Microsoft.Win32;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkflowEditorControl : UserControl
    {
        private readonly PluginRegistry _plugins = HostProvider.Instance.PluginStore;
        private readonly CommandWorkflowRepository _repository;
        private readonly CommandWorkflowEditorSession _session;
        private readonly CommandWorkflowExecutionReportRepository _reports;
        private readonly CommandWorkflowRunSession _runSession;
        private CommandWorkflowExecutionReview? _executionReview;
        private CommandWorkflowImportReview? _importReview;
        private int _reportListVersion;
        private int _reportLoadVersion;
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
            _reports = new CommandWorkflowExecutionReportRepository(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LongBetterWindows",
                "WorkflowReports"));
            _runSession = new CommandWorkflowRunSession(_plugins, _reports);
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
            _runSession.CancelReview();
            _runSession.CancelExecution();
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
            await RefreshReportsAsync(item.Id);
        }

        private async void CompactWorkflowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rendering || CompactWorkflowCombo.SelectedItem is not WorkflowListItem item) return;
            await _session.LoadAsync(item.Id);
            RenderEditor();
            await RefreshReportsAsync(item.Id);
        }

        private void NewWorkflow_Click(object sender, RoutedEventArgs e)
        {
            _importReview = null;
            var suffix = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            _session.StartNew($"workflow.{suffix}", "新组合动作");
            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            ReportList.ItemsSource = null;
            ReportTimeline.ItemsSource = null;
            RenderEditor();
            WorkflowNameBox.Focus();
            WorkflowNameBox.SelectAll();
        }

        private async void ImportWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择外部工作流",
                Filter = "Long 工作流 (*.workflow.json)|*.workflow.json|JSON 文件 (*.json)|*.json",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;
            var review = await _session.PreviewImportAsync(dialog.FileName);
            if (!review.IsSuccess)
            {
                MessageBox.Show(
                    review.Error ?? "外部工作流无法读取。",
                    "导入工作流",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _importReview = review;
            RenderImportReview();
        }

        private void CancelImport_Click(object sender, RoutedEventArgs e)
        {
            _importReview = null;
            RenderEditor();
        }

        private void AdoptImport_Click(object sender, RoutedEventArgs e)
        {
            var review = _importReview;
            if (review is null) return;
            if (_session.State.IsDirty)
            {
                var answer = MessageBox.Show(
                    "当前草稿有未保存的更改。采用外部工作流会替换当前草稿，是否继续？",
                    "采用外部工作流",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            if (!_session.AdoptImport(review))
            {
                MessageBox.Show(
                    _session.State.Error ?? "外部工作流无法采用。",
                    "导入工作流",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _importReview = null;
            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            ReportList.ItemsSource = null;
            ReportTimeline.ItemsSource = null;
            RenderEditor();
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
            InvalidateExecutionReview();
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

        private void InvocationEditor_Changed(object? sender, EventArgs e)
        {
            if (_rendering
                || sender is not WorkflowInvocationEditorControl { Editor: { } item }
                || !ApplyInvocation(item)) return;
            InvalidateExecutionReview();
            SensitiveInputCheckBox.Visibility = CommandWorkflowDocumentCodec.ContainsSensitiveInputs(
                _session.State.Draft!) ? Visibility.Visible : Visibility.Collapsed;
            RenderStatus();
        }

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
            {
                await RefreshListAsync(_session.State.Draft?.Id);
                await RefreshReportsAsync(_session.State.Draft!.Id);
            }
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
            ReportList.ItemsSource = null;
            ReportTimeline.ItemsSource = null;
        }

        private void PrepareRun_Click(object sender, RoutedEventArgs e)
        {
            var state = _session.State;
            if (state.Draft is null
                || state.ExistingDefinitionSha256 is null
                || state.IsDirty) return;
            _executionReview = _runSession.Prepare(state.Draft);
            if (!_executionReview.IsValid)
            {
                ExecutionResultTitle.Text = "无法准备执行";
                ExecutionResultDetail.Text = string.Join(Environment.NewLine, _executionReview.Issues);
                ExecutionResultPanel.Visibility = Visibility.Visible;
                return;
            }
            ExecutionReviewSummary.Text = _executionReview.ContainsMutatingSteps
                ? $"{_executionReview.StepCount} 个步骤，包含系统或文件修改；失败策略为“{FailureLabel(state.Draft.FailureMode)}”。"
                : $"{_executionReview.StepCount} 个只读步骤；失败策略为“{FailureLabel(state.Draft.FailureMode)}”。";
            ExecutionPermissionList.ItemsSource = _executionReview.Permissions
                .Select(permission => new PermissionReviewItem(
                    $"{permission.PluginId}  v{permission.PluginVersion}",
                    permission.Capabilities.Count == 0
                        ? "无额外能力"
                        : string.Join("、", permission.Capabilities)))
                .ToList();
            ExecutionResultPanel.Visibility = Visibility.Collapsed;
            ExecutionReviewPanel.Visibility = Visibility.Visible;
        }

        private void CancelRunReview_Click(object sender, RoutedEventArgs e)
        {
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
        }

        private async void ConfirmRun_Click(object sender, RoutedEventArgs e)
        {
            var draft = _session.State.Draft;
            var review = _executionReview;
            if (draft is null || review is null) return;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
            ExecutionRunningPanel.Visibility = Visibility.Visible;
            SetEditingEnabled(false);
            try
            {
                var result = await _runSession.ExecuteApprovedAsync(
                    draft,
                    review.Fingerprint,
                    includeSensitiveMessages: false);
                if (!result.IsAccepted || result.Execution is null)
                {
                    ExecutionResultTitle.Text = "执行未开始";
                    ExecutionResultDetail.Text = result.Error ?? "执行批准已经失效。";
                }
                else
                {
                    ExecutionResultTitle.Text = StatusLabel(result.Execution.Status);
                    ExecutionResultDetail.Text = result.ReportSave?.IsSuccess == true
                        ? $"已记录 {result.Execution.Events.Count} 个脱敏事件。"
                        : $"执行已结束，但报告保存失败：{result.ReportSave?.Error}";
                    await RefreshReportsAsync(draft.Id);
                    ReportsExpander.IsExpanded = true;
                }
                ExecutionResultPanel.Visibility = Visibility.Visible;
            }
            finally
            {
                _executionReview = null;
                ExecutionRunningPanel.Visibility = Visibility.Collapsed;
                SetEditingEnabled(true);
                RenderStatus();
            }
        }

        private void CancelExecution_Click(object sender, RoutedEventArgs e)
            => _runSession.CancelExecution();

        private async Task RefreshReportsAsync(string workflowId)
        {
            var version = Interlocked.Increment(ref _reportListVersion);
            var result = await _reports.ListAsync(workflowId);
            if (version != _reportListVersion) return;
            if (!result.IsSuccess)
            {
                ReportDetailTitle.Text = "执行记录不可用";
                ReportDetailMeta.Text = result.Error;
                return;
            }
            ReportList.ItemsSource = result.Reports.Select(ReportListItem.From).ToList();
            if (result.Issues.Count > 0)
                ReportDetailMeta.Text = $"{result.Issues.Count} 个报告文件未能载入";
        }

        private async void ReportList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReportList.SelectedItem is not ReportListItem item) return;
            var version = Interlocked.Increment(ref _reportLoadVersion);
            var result = await _reports.LoadAsync(item.ReportId);
            if (version != _reportLoadVersion) return;
            if (!result.IsSuccess)
            {
                ReportDetailTitle.Text = "报告读取失败";
                ReportDetailMeta.Text = result.Error;
                ReportTimeline.ItemsSource = null;
                return;
            }
            var report = result.Report!;
            ReportDetailTitle.Text = StatusLabel(report.Status);
            var messageState = report.MessagesIncluded ? "消息未在界面展示" : "消息已脱敏";
            ReportDetailMeta.Text = $"{report.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {report.Events.Count} 个事件 · {messageState}";
            ReportTimeline.ItemsSource = report.Events.Select(item => new TimelineItem(
                item.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                EventLabel(item.Kind),
                item.StepId ?? "工作流"))
                .ToList();
        }

        private void RenderEditor()
        {
            InvalidateExecutionReview();
            _rendering = true;
            try
            {
                if (_importReview is not null)
                {
                    RenderImportReview();
                    return;
                }
                SetImportReviewMode(false);
                var state = _session.State;
                var draft = state.Draft;
                ImportReviewPanel.Visibility = Visibility.Collapsed;
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
                    PrimaryInput = CreateInvocationEditor(
                        step.Id,
                        WorkflowCommandRole.Primary,
                        "命令输入",
                        step.Command),
                    CompensationInput = CreateInvocationEditor(
                        step.Id,
                        WorkflowCommandRole.Compensation,
                        "补偿输入",
                        step.Compensation),
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

        private void RenderImportReview()
        {
            var review = _importReview;
            if (review?.Workflow is null) return;
            SetImportReviewMode(true);
            EmptyEditor.Visibility = Visibility.Collapsed;
            EditorBody.Visibility = Visibility.Collapsed;
            ImportReviewPanel.Visibility = Visibility.Visible;
            ImportReviewName.Text = $"{review.Workflow.Name}  ·  {review.Workflow.Id}";
            ImportReviewSource.Text = $"{review.SourcePath}\nSHA-256  {review.DefinitionSha256}";
            ImportReviewSummary.Text = $"{review.Workflow.Steps.Count} 个步骤 · "
                + $"{FailureLabel(review.Workflow.FailureMode)} · "
                + (review.ContainsSensitiveInputs ? "包含潜在敏感输入" : "不包含持久化输入");
            ImportReviewTrust.Text = review.TrustLevel switch
            {
                WorkflowDocumentTrustLevel.TrustedSource => "来源与已信任定义匹配",
                WorkflowDocumentTrustLevel.LocalManaged => "本机托管来源",
                _ => "未信任的外部来源",
            };
            var preflight = review.Preflight;
            ImportReviewPreflight.Text = preflight?.IsValid == true
                ? $"预检通过；采用后仍需保存，并在每次执行前重新批准 {preflight.Permissions.Count} 个插件。"
                : "预检未通过；采用后可编辑修正："
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, preflight?.Issues ?? Array.Empty<string>());
        }

        private void SetImportReviewMode(bool active)
        {
            WorkflowList.IsEnabled = !active;
            CompactWorkflowCombo.IsEnabled = !active;
            RefreshWorkflowButton.IsEnabled = !active;
            ImportWorkflowButton.IsEnabled = !active;
            NewWorkflowButton.IsEnabled = !active;
            CompactRefreshButton.IsEnabled = !active;
            CompactImportButton.IsEnabled = !active;
            CompactNewButton.IsEnabled = !active;
        }

        private WorkflowInvocationEditorModel CreateInvocationEditor(
            string stepId,
            WorkflowCommandRole role,
            string roleLabel,
            WorkflowCommand? command)
        {
            var invocation = command?.Invocation ?? new PluginCommandInvocation();
            var descriptor = command is null ? null : _plugins.Commands.Get(command.CommandKey);
            var options = (descriptor?.Command.AcceptedInputs ?? [AcceptedInputType.None])
                .Distinct()
                .Select(type => new WorkflowInputTypeOption(type, InputTypeLabel(type)))
                .ToList();
            return new WorkflowInvocationEditorModel
            {
                StepId = stepId,
                Role = role,
                RoleLabel = roleLabel,
                InputType = invocation.InputType,
                InputOptions = options,
                Text = invocation.Text ?? string.Empty,
                Paths = invocation.Paths.ToArray(),
                ImagePng = invocation.ImagePng?.ToArray(),
                Arguments = new Dictionary<string, string>(invocation.Arguments, StringComparer.Ordinal),
            };
        }

        private bool ApplyInvocation(WorkflowInvocationEditorModel item)
            => _session.UpdateInvocation(
                item.StepId,
                item.Role,
                item.InputType,
                item.Text,
                item.Paths,
                item.ImagePng,
                item.Arguments);

        private void RenderStatus()
        {
            var state = _session.State;
            EditorDirtyText.Text = state.ExistingDefinitionSha256 is null
                ? "尚未保存"
                : state.IsDirty ? "有未保存的更改" : "已保存到本机";
            SaveWorkflowButton.IsEnabled = state.CanSave && state.IsDirty;
            PrepareRunButton.IsEnabled = state.Preflight?.IsValid == true
                && state.ExistingDefinitionSha256 is not null
                && !state.IsDirty
                && !_runSession.IsRunning;
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

        private void SetEditingEnabled(bool enabled)
        {
            WorkflowList.IsEnabled = enabled;
            CompactWorkflowCombo.IsEnabled = enabled;
            RefreshWorkflowButton.IsEnabled = enabled;
            ImportWorkflowButton.IsEnabled = enabled;
            NewWorkflowButton.IsEnabled = enabled;
            CompactRefreshButton.IsEnabled = enabled;
            CompactImportButton.IsEnabled = enabled;
            CompactNewButton.IsEnabled = enabled;
            WorkflowIdBox.IsEnabled = enabled && _session.State.ExistingDefinitionSha256 is null;
            WorkflowNameBox.IsEnabled = enabled;
            FailureModeCombo.IsEnabled = enabled;
            AddCommandCombo.IsEnabled = enabled;
            AddStepButton.IsEnabled = enabled;
            StepsList.IsEnabled = enabled;
            DeleteWorkflowButton.IsEnabled = enabled && _session.State.ExistingDefinitionSha256 is not null;
            SaveWorkflowButton.IsEnabled = enabled && _session.State.CanSave && _session.State.IsDirty;
            PrepareRunButton.IsEnabled = enabled;
        }

        private void InvalidateExecutionReview()
        {
            if (ExecutionReviewPanel.Visibility != Visibility.Visible) return;
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
        }

        private static string FailureLabel(WorkflowFailureMode mode)
            => mode == WorkflowFailureMode.Compensate ? "失败时回滚" : "失败时停止";

        private static string StatusLabel(WorkflowExecutionStatus status)
            => status switch
            {
                WorkflowExecutionStatus.Completed => "执行完成",
                WorkflowExecutionStatus.Compensated => "失败后已回滚",
                WorkflowExecutionStatus.CompensationFailed => "回滚未完全成功",
                WorkflowExecutionStatus.Cancelled => "执行已取消",
                WorkflowExecutionStatus.Rejected => "执行已拒绝",
                _ => "执行失败",
            };

        private static string InputTypeLabel(AcceptedInputType inputType)
            => inputType switch
            {
                AcceptedInputType.None => "无输入",
                AcceptedInputType.Text => "文本",
                AcceptedInputType.Url => "URL",
                AcceptedInputType.Image => "PNG 图片",
                AcceptedInputType.File => "单个文件",
                AcceptedInputType.Files => "多个文件",
                AcceptedInputType.Folder => "文件夹",
                AcceptedInputType.Clipboard => "剪贴板文本快照",
                _ => "资源管理器选区",
            };

        private static string EventLabel(WorkflowExecutionEventKind kind)
            => kind switch
            {
                WorkflowExecutionEventKind.PreflightPassed => "预检通过",
                WorkflowExecutionEventKind.AuthorizationApproved => "批准已确认",
                WorkflowExecutionEventKind.StepStarted => "步骤开始",
                WorkflowExecutionEventKind.StepSucceeded => "步骤成功",
                WorkflowExecutionEventKind.StepFailed => "步骤失败",
                WorkflowExecutionEventKind.StepCancelled => "步骤取消",
                WorkflowExecutionEventKind.CompensationStarted => "开始回滚",
                WorkflowExecutionEventKind.CompensationSucceeded => "回滚成功",
                WorkflowExecutionEventKind.CompensationFailed => "回滚失败",
                WorkflowExecutionEventKind.WorkflowCompleted => "流程完成",
                _ => "流程拒绝",
            };

        private sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;
        private sealed record PermissionReviewItem(string Plugin, string Capabilities);
        private sealed record TimelineItem(string Time, string Kind, string Step);
        private sealed record ReportListItem(string ReportId, string Status, string Detail)
        {
            public static ReportListItem From(WorkflowExecutionReportSummary summary)
                => new(
                    summary.ReportId,
                    StatusLabel(summary.Status),
                    $"{summary.StartedAt.ToLocalTime():MM-dd HH:mm} · {summary.EventCount} 事件");
        }
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
            public bool HasCompensation => !string.IsNullOrWhiteSpace(CompensationKey);
            public bool CanMoveUp { get; init; }
            public bool CanMoveDown { get; init; }
            public required IReadOnlyList<CommandOption> CommandOptions { get; init; }
            public required IReadOnlyList<CommandOption> CompensationOptions { get; init; }
            public required IReadOnlyList<EnumOption<WorkflowStepEffect>> EffectOptions { get; init; }
            public required WorkflowInvocationEditorModel PrimaryInput { get; init; }
            public required WorkflowInvocationEditorModel CompensationInput { get; init; }
        }
    }
}
