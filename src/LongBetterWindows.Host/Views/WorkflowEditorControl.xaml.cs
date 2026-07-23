using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkflowEditorControl : UserControl
    {
        internal event EventHandler? ExecutionReviewClosed;
        internal event Action<bool>? ResponsiveLayoutChanged;
        internal event Action<WorkflowExecutionResultState>? ExecutionResultChanged;
        internal event EventHandler? TerminalOutputsCleared;
        private readonly PluginRegistry _plugins = HostProvider.Instance.PluginStore;
        private readonly CommandWorkflowRepository _repository;
        private readonly CommandWorkflowEditorSession _session;
        private readonly CommandWorkflowExecutionReportRepository _reports;
        private readonly CommandWorkflowRunSession _runSession;
        private readonly WorkflowTerminalOutputExporter _terminalOutputExporter = new();
        private CommandWorkflowExecutionReview? _executionReview;
        private bool? _isCompactLayout;

        internal bool IsCompactLayout => _isCompactLayout ?? ActualWidth < 700;
        internal double LayoutWidth => ActualWidth;
        private CommandWorkflowImportReview? _importReview;
        private int _reportListVersion;
        private int _reportLoadVersion;
        private bool _rendering = true;
        private bool _subscribed;

        public WorkflowEditorControl()
        {
            InitializeComponent();
            _repository = ServicesInitializer.Workflows;
            _session = new CommandWorkflowEditorSession(_plugins, _repository);
            _reports = new CommandWorkflowExecutionReportRepository(
                ServicesInitializer.WorkflowReportsDirectory);
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
                || sender is not WorkflowInvocationEditorControl { Editor: { } item }) return;
            InvalidateExecutionReview();
            if (ApplyInvocation(item))
            {
                SensitiveInputCheckBox.Visibility = CommandWorkflowDocumentCodec.ContainsSensitiveInputs(
                    _session.State.Draft!) ? Visibility.Visible : Visibility.Collapsed;
            }
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

        private async void ExportWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var state = _session.State;
            if (state.Draft is null
                || state.ExistingDefinitionSha256 is null
                || state.IsDirty
                || HasInvalidInvocationEditors()) return;
            var dialog = new SaveFileDialog
            {
                Title = "导出组合动作",
                Filter = "Long 工作流 (*.workflow.json)|*.workflow.json",
                FileName = $"{state.Draft.Id}.workflow.json",
                AddExtension = true,
                DefaultExt = ".workflow.json",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog() != true) return;
            ExportWorkflowButton.IsEnabled = false;
            var result = await _session.ExportCurrentAsync(dialog.FileName);
            RenderStatus();
            MessageBox.Show(
                result.IsSuccess
                    ? $"组合动作已导出到：{result.Path}"
                    : result.Error ?? "组合动作导出失败。",
                "导出组合动作",
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
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

        internal async Task<string?> OpenExecutionReviewAsync(
            string workflowId,
            string? expectedStateFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
            if (!await _session.LoadAsync(workflowId, cancellationToken))
                return _session.State.Error ?? "组合动作已经失效。";

            await RefreshListAsync(workflowId);
            RenderEditor();
            await RefreshReportsAsync(workflowId);
            return PrepareRun(expectedStateFingerprint)
                ? null
                : _executionReview is not null
                    ? string.Join(" ", _executionReview.Issues)
                    : _session.State.Error ?? "组合动作未能通过实时预检。";
        }

        private void PrepareRun_Click(object sender, RoutedEventArgs e)
            => PrepareRun();

        private bool PrepareRun(string? expectedStateFingerprint = null)
        {
            var state = _session.State;
            if (state.Draft is null
                || state.ExistingDefinitionSha256 is null
                || state.IsDirty) return false;
            ClearTerminalOutputs();
            TerminalOutputApprovalCheckBox.IsChecked = false;
            _executionReview = _runSession.Prepare(
                state.Draft,
                expectedStateFingerprint);
            if (!_executionReview.IsValid)
            {
                var failure = WorkflowExecutionPresentation.DescribePrepareFailure(_executionReview);
                ExecutionResultTitle.Text = failure.Title;
                ExecutionResultDetail.Text = failure.Detail;
                ExecutionOutputList.ItemsSource = failure.Outputs;
                ExecutionOutputList.Visibility = failure.HasOutputs ? Visibility.Visible : Visibility.Collapsed;
                ExecutionResultPanel.Visibility = Visibility.Visible;
                return false;
            }
            var presentation = WorkflowExecutionPresentation.DescribeReview(
                _executionReview,
                state.Draft.FailureMode);
            ExecutionReviewSummary.Text = presentation.Summary;
            ExecutionPermissionList.ItemsSource = presentation.Permissions;
            ExecutionResultPanel.Visibility = Visibility.Collapsed;
            EditorConfigurationPanel.Visibility = Visibility.Collapsed;
            ExecutionReviewPanel.Visibility = Visibility.Visible;
            return true;
        }

        private void CancelRunReview_Click(object sender, RoutedEventArgs e)
            => CancelExecutionReview();

        internal bool CancelExecutionReview()
        {
            if (ExecutionReviewPanel.Visibility != Visibility.Visible) return false;
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
            EditorConfigurationPanel.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() =>
                Keyboard.Focus(PrepareRunButton)), DispatcherPriority.Input);
            ExecutionReviewClosed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private async void ConfirmRun_Click(object sender, RoutedEventArgs e)
            => await ConfirmExecutionReviewAsync();

        internal bool ToggleTerminalOutputApproval()
        {
            if (ExecutionReviewPanel.Visibility != Visibility.Visible) return false;
            TerminalOutputApprovalCheckBox.IsChecked =
                TerminalOutputApprovalCheckBox.IsChecked != true;
            return TerminalOutputApprovalCheckBox.IsChecked == true;
        }

        internal async Task<bool> ConfirmExecutionReviewAsync()
        {
            var draft = _session.State.Draft;
            var review = _executionReview;
            if (draft is null
                || review is null
                || ExecutionReviewPanel.Visibility != Visibility.Visible) return false;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
            ExecutionReviewClosed?.Invoke(this, EventArgs.Empty);
            ExecutionRunningPanel.Visibility = Visibility.Visible;
            SetEditingEnabled(false);
            try
            {
                var result = await _runSession.ExecuteApprovedAsync(
                    draft,
                    review.Fingerprint,
                    includeSensitiveMessages: false,
                    includeTerminalOutputValues: TerminalOutputApprovalCheckBox.IsChecked == true);
                var presentation = WorkflowExecutionPresentation.DescribeRunResult(result);
                ExecutionResultTitle.Text = presentation.Title;
                ExecutionResultDetail.Text = presentation.Detail;
                ExecutionOutputList.ItemsSource = presentation.Outputs;
                ExecutionOutputList.Visibility = presentation.HasOutputs ? Visibility.Visible : Visibility.Collapsed;
                TerminalOutputList.ItemsSource = presentation.TerminalOutputs;
                TerminalOutputPanel.Visibility = presentation.HasTerminalOutputs ? Visibility.Visible : Visibility.Collapsed;
                ExecutionResultChanged?.Invoke(new WorkflowExecutionResultState(
                    presentation.Title,
                    presentation.TerminalOutputs.Sum(output => output.Value.Length),
                    presentation.HasTerminalOutputs));
                if (result.IsAccepted && result.Execution is not null)
                {
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
            return true;
        }

        private void CancelExecution_Click(object sender, RoutedEventArgs e)
            => _runSession.CancelExecution();

        private void CopyTerminalOutput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WorkflowTerminalOutputItem output }) return;
            try
            {
                if (output.Value.Length == 0)
                    Clipboard.Clear();
                else
                    Clipboard.SetText(output.Value);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"无法复制终端输出：{exception.Message}",
                    "Long Better Windows",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ClearTerminalOutputs_Click(object sender, RoutedEventArgs e)
            => ClearTerminalOutputs();

        private async void ExportTerminalOutput_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WorkflowTerminalOutputItem item }) return;
            var dialog = new SaveFileDialog
            {
                Title = "导出终端输出",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"{item.Source.StepId}-{item.Source.OutputKey}.txt",
                AddExtension = true,
                DefaultExt = ".txt",
                OverwritePrompt = false,
            };
            if (dialog.ShowDialog() != true) return;

            var review = _terminalOutputExporter.Prepare(item.Source, dialog.FileName);
            if (!review.IsValid)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, review.Issues),
                    "无法准备终端输出导出",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var approved = MessageBox.Show(
                "即将把终端输出明文写入磁盘。\n\n"
                    + $"输出：{item.Source.StepId} / {item.Source.OutputKey}\n"
                    + $"类型：{item.Source.Type}\n"
                    + $"UTF-8 大小：{review.Utf8ByteCount:N0} 字节\n"
                    + $"SHA-256：{review.ValueSha256}\n"
                    + $"目标：{review.DestinationPath}\n\n"
                    + "现有文件不会被覆盖。是否批准本次导出？",
                "批准终端输出导出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (approved != MessageBoxResult.Yes) return;

            var result = await _terminalOutputExporter.ExportApprovedAsync(
                item.Source,
                dialog.FileName,
                review.Fingerprint);
            MessageBox.Show(
                result.IsSuccess
                    ? $"终端输出已导出到：{result.Path}\nSHA-256：{result.ValueSha256}"
                    : result.Error ?? "终端输出导出失败。",
                "导出终端输出",
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        internal bool ClearTerminalOutputs()
        {
            var hadOutputs = TerminalOutputPanel.Visibility == Visibility.Visible;
            TerminalOutputList.ItemsSource = null;
            TerminalOutputPanel.Visibility = Visibility.Collapsed;
            if (hadOutputs) TerminalOutputsCleared?.Invoke(this, EventArgs.Empty);
            return hadOutputs;
        }

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
            ReportList.ItemsSource = WorkflowExecutionPresentation.ToReportListItems(result.Reports);
            if (result.Issues.Count > 0)
                ReportDetailMeta.Text = $"{result.Issues.Count} 个报告文件未能载入";
        }

        private async void ReportList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReportList.SelectedItem is not WorkflowReportListItem item) return;
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
            var presentation = WorkflowExecutionPresentation.DescribeReport(result.Report!);
            ReportDetailTitle.Text = presentation.Title;
            ReportDetailMeta.Text = presentation.Meta;
            ReportTimeline.ItemsSource = presentation.Timeline;
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
                EditorConfigurationPanel.Visibility = Visibility.Visible;

                EditorHeading.Text = draft.Name;
                WorkflowIdBox.Text = draft.Id;
                WorkflowIdBox.IsEnabled = state.ExistingDefinitionSha256 is null;
                WorkflowNameBox.Text = draft.Name;
                FailureModeCombo.SelectedValue = draft.FailureMode;
                var commands = _session.AvailableCommands.Select(CommandOption.From).ToList();
                var compensationOptions = new[] { new CommandOption(string.Empty, "不设置") }
                    .Concat(commands)
                    .ToList();
                var stepEditors = new List<StepEditorItem>();
                var priorOutputs = new List<WorkflowBindingOutputOption>();
                for (var index = 0; index < draft.Steps.Count; index++)
                {
                    var step = draft.Steps[index];
                    var primaryOutputs = GetDeclaredOutputOptions(step.Id, step.Command);
                    stepEditors.Add(new StepEditorItem
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
                            step.Command,
                            priorOutputs),
                        CompensationInput = CreateInvocationEditor(
                            step.Id,
                            WorkflowCommandRole.Compensation,
                            "补偿输入",
                            step.Compensation,
                            priorOutputs.Concat(primaryOutputs).ToArray()),
                    });
                    priorOutputs.AddRange(primaryOutputs);
                }
                StepsList.ItemsSource = stepEditors;
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
            WorkflowCommand? command,
            IReadOnlyList<WorkflowBindingOutputOption> availableOutputs)
        {
            var invocation = command?.Invocation ?? new PluginCommandInvocation();
            var descriptor = command is null ? null : _plugins.Commands.Get(command.CommandKey);
            var options = (descriptor?.Command.AcceptedInputs ?? [AcceptedInputType.None])
                .Distinct()
                .Select(type => new WorkflowInputTypeOption(type, InputTypeLabel(type)))
                .ToList();
            var editor = new WorkflowInvocationEditorModel
            {
                StepId = stepId,
                Role = role,
                RoleLabel = roleLabel,
                BindingEditor = new WorkflowBindingEditorModel(availableOutputs, invocation.InputType),
                InputType = invocation.InputType,
                InputOptions = options,
                Text = invocation.Text ?? string.Empty,
                Paths = invocation.Paths.ToArray(),
                ImagePng = invocation.ImagePng?.ToArray(),
            };
            editor.LoadArguments(invocation.Arguments);
            editor.BindingEditor.LoadBindings(command?.Bindings);
            return editor;
        }

        private IReadOnlyList<WorkflowBindingOutputOption> GetDeclaredOutputOptions(
            string stepId,
            WorkflowCommand? command)
        {
            var descriptor = command is null ? null : _plugins.Commands.Get(command.CommandKey);
            return descriptor?.Command.Outputs.Select(output => new WorkflowBindingOutputOption(
                    stepId,
                    output.Key,
                    output.Type,
                    output.Description))
                .ToArray()
                ?? Array.Empty<WorkflowBindingOutputOption>();
        }

        private bool ApplyInvocation(WorkflowInvocationEditorModel item)
        {
            if (!item.TryBuildArguments(out var arguments)
                || !item.BindingEditor.TryBuildBindings(out var bindings)) return false;
            if (!_session.UpdateInvocation(
                item.StepId,
                item.Role,
                item.InputType,
                item.Text,
                item.Paths,
                item.ImagePng,
                arguments)) return false;
            return _session.UpdateBindings(item.StepId, item.Role, bindings);
        }

        private void RenderStatus()
        {
            var state = _session.State;
            var hasInvalidEditors = HasInvalidInvocationEditors();
            EditorDirtyText.Text = state.ExistingDefinitionSha256 is null
                ? "尚未保存"
                : state.IsDirty ? "有未保存的更改" : "已保存到本机";
            SaveWorkflowButton.IsEnabled = state.CanSave && state.IsDirty && !hasInvalidEditors;
            ExportWorkflowButton.IsEnabled = state.ExistingDefinitionSha256 is not null
                && !state.IsDirty
                && !hasInvalidEditors;
            PrepareRunButton.IsEnabled = state.Preflight?.IsValid == true
                && state.ExistingDefinitionSha256 is not null
                && !state.IsDirty
                && !hasInvalidEditors
                && !_runSession.IsRunning;
            if (hasInvalidEditors)
            {
                PreflightTitle.Text = "输入或绑定需要修正";
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Danger");
                PreflightDetail.Text = "检查高级参数和步骤输出绑定；修正后才能保存或执行。";
                return;
            }
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

        private bool HasInvalidInvocationEditors()
            => StepsList.ItemsSource is IEnumerable<StepEditorItem> steps
                && steps.Any(step => step.PrimaryInput.HasArgumentError
                    || step.PrimaryInput.BindingEditor.HasError
                    || (step.HasCompensation && (step.CompensationInput.HasArgumentError
                        || step.CompensationInput.BindingEditor.HasError)));

        private void SetListStatus(string text, bool isError)
        {
            ListStatusText.Text = text;
            ListStatusText.Foreground = (System.Windows.Media.Brush)FindResource(
                isError ? "Long.Brush.State.Danger" : "Long.Brush.Text.Muted");
        }

        private void ApplyResponsiveLayout(double width)
        {
            var compact = width < 700;
            if (_isCompactLayout == compact) return;
            _isCompactLayout = compact;
            WorkflowListColumn.Width = new GridLength(compact ? 0 : 232);
            WorkflowDividerColumn.Width = new GridLength(compact ? 0 : 1);
            WorkflowGapColumn.Width = new GridLength(compact ? 0 : 20);
            CompactWorkflowBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            ResponsiveLayoutChanged?.Invoke(compact);
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
            ExportWorkflowButton.IsEnabled = enabled
                && _session.State.ExistingDefinitionSha256 is not null
                && !_session.State.IsDirty
                && !HasInvalidInvocationEditors();
            SaveWorkflowButton.IsEnabled = enabled && _session.State.CanSave && _session.State.IsDirty;
            PrepareRunButton.IsEnabled = enabled;
        }

        private void InvalidateExecutionReview()
        {
            ClearTerminalOutputs();
            if (ExecutionReviewPanel.Visibility != Visibility.Visible) return;
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
        }

        private static string FailureLabel(WorkflowFailureMode mode)
            => WorkflowExecutionPresentation.FailureLabel(mode);

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

    internal sealed record WorkflowExecutionResultState(
        string Title,
        int TerminalOutputLength,
        bool HasTerminalOutputs);
}
