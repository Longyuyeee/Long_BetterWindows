using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Microsoft.Win32;
using Serilog;

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
        private CommandWorkflowRunResult? _lastRunResult;
        private IReadOnlyList<WorkflowExecutionReportSummary> _reportSummaries =
            Array.Empty<WorkflowExecutionReportSummary>();
        private WorkflowExecutionReportDocument? _selectedReport;
        private bool? _isCompactLayout;

        internal bool IsCompactLayout => _isCompactLayout ?? ActualWidth < 700;
        internal double LayoutWidth => ActualWidth;
        private CommandWorkflowImportReview? _importReview;
        private bool _reviewingTemplate;
        private int _reportListVersion;
        private int _reportLoadVersion;
        private bool _rendering = true;
        private bool _subscribed;
        private bool _languageSubscribed;

        public WorkflowEditorControl()
        {
            InitializeComponent();
            _repository = ServicesInitializer.Workflows;
            _session = new CommandWorkflowEditorSession(
                _plugins,
                _repository,
                ServicesInitializer.WorkflowTemplates);
            _reports = new CommandWorkflowExecutionReportRepository(
                ServicesInitializer.WorkflowReportsDirectory);
            _runSession = new CommandWorkflowRunSession(_plugins, _reports);
            FailureModeCombo.ItemsSource = CreateFailureOptions();
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            _rendering = false;
        }

        private static IReadOnlyList<EnumOption<WorkflowFailureMode>> CreateFailureOptions() =>
        [
            new(WorkflowFailureMode.Stop, I18n("workflow.failure.stop")),
            new(WorkflowFailureMode.Compensate, I18n("workflow.failure.compensate")),
        ];

        private static IReadOnlyList<EnumOption<WorkflowStepEffect>> CreateEffectOptions() =>
        [
            new(WorkflowStepEffect.ReadOnly, I18n("workflow.effect.readOnly")),
            new(WorkflowStepEffect.Mutating, I18n("workflow.effect.mutating")),
        ];

        private async void WorkflowEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
            {
                _plugins.PluginsChanged += PluginsChanged;
                _subscribed = true;
            }
            if (!_languageSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged += OnLanguageChanged;
                _languageSubscribed = true;
            }
            await RefreshListAsync();
            RefreshCommandOptions();
        }

        private void WorkflowEditorControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _runSession.CancelReview();
            _runSession.CancelExecution();
            if (_subscribed)
            {
                _plugins.PluginsChanged -= PluginsChanged;
                _subscribed = false;
            }
            if (_languageSubscribed)
            {
                ServicesInitializer.I18n.LanguageChanged -= OnLanguageChanged;
                _languageSubscribed = false;
            }
        }

        private async void OnLanguageChanged(string language)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnLanguageChanged(language));
                return;
            }
            var selectedFailureMode = FailureModeCombo.SelectedValue;
            FailureModeCombo.ItemsSource = CreateFailureOptions();
            FailureModeCombo.SelectedValue = selectedFailureMode;
            await RefreshListAsync(_session.State.Draft?.Id);
            if (_importReview is not null)
            {
                RenderImportReview();
                return;
            }
            if (ExecutionReviewPanel.Visibility == Visibility.Visible
                || ExecutionRunningPanel.Visibility == Visibility.Visible
                || ExecutionResultPanel.Visibility == Visibility.Visible)
            {
                RefreshLocalizedExecutionState();
                RefreshLocalizedReports();
                return;
            }
            RenderEditor();
        }

        private void PluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(PluginsChanged);
                return;
            }
            if (ExecutionReviewPanel.Visibility == Visibility.Visible
                || _runSession.IsRunning)
            {
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
                SetListStatus(
                    I18n(WorkflowErrorPresentation.GetResourceKey(result.ErrorCode)),
                    isError: true);
                return;
            }
            var items = result.Workflows.Select(WorkflowListItem.From).ToList();
            _rendering = true;
            WorkflowList.ItemsSource = items;
            CompactWorkflowCombo.ItemsSource = items;
            WorkflowCountText.Text = string.Format(I18n("workflow.list.count"), items.Count);
            SetListStatus(
                result.Issues.Count == 0
                    ? I18n("workflow.list.localManaged")
                    : string.Format(I18n("workflow.list.loadIssues"), result.Issues.Count),
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
            _session.StartNew($"workflow.{suffix}", I18n("workflow.new.defaultName"));
            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            ClearReportPresentation();
            RenderEditor();
            WorkflowNameBox.Focus();
            WorkflowNameBox.SelectAll();
        }

        private async void ImportWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = I18n("workflow.import.dialog.title"),
                Filter = I18n("workflow.import.dialog.filter"),
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;
            var review = await _session.PreviewImportAsync(dialog.FileName);
            if (!review.IsSuccess)
            {
                Log.Warning(
                    "External workflow import preview failed ({ErrorCode}): {Error}",
                    review.ErrorCode,
                    review.Error);
                MessageBox.Show(
                    I18n(WorkflowErrorPresentation.GetResourceKey(review.ErrorCode)),
                    I18n("workflow.import.dialog.errorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _reviewingTemplate = false;
            _importReview = review;
            RenderImportReview();
        }

        private async void TemplateWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var result = await _session.ListTemplatesAsync();
            if (!result.IsSuccess)
            {
                Log.Warning(
                    "Workflow template catalog could not be listed ({ErrorCode}): {Error}",
                    result.ErrorCode,
                    result.Error);
                MessageBox.Show(
                    I18n(WorkflowErrorPresentation.GetResourceKey(result.ErrorCode)),
                    I18n("workflow.template.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (result.Templates.Count == 0)
            {
                MessageBox.Show(
                    I18n("workflow.template.empty"),
                    I18n("workflow.template.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = sender as UIElement,
                Placement = PlacementMode.Bottom,
            };
            foreach (var template in result.Templates)
            {
                var trust = template.TrustLevel == WorkflowDocumentTrustLevel.TrustedSource
                    ? I18n("workflow.template.trust.trusted")
                    : I18n("workflow.template.trust.review");
                var item = new MenuItem
                {
                    Header = template.Name,
                    ToolTip = Format(
                        "workflow.template.itemDetail",
                        template.StepCount,
                        trust,
                        template.Id),
                    Tag = template,
                };
                item.Click += TemplateMenuItem_Click;
                menu.Items.Add(item);
            }
            if (result.Issues.Count > 0)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem
                {
                    Header = Format(
                        "workflow.template.invalidCount",
                        result.Issues.Count),
                    IsEnabled = false,
                });
            }
            menu.IsOpen = true;
        }

        private async void TemplateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem
                {
                    Tag: CommandWorkflowTemplateSummary template,
                })
            {
                return;
            }
            var review = await _session.PreviewTemplateAsync(
                template.Key,
                template.DefinitionSha256);
            if (!review.IsSuccess)
            {
                Log.Warning(
                    "Workflow template preview failed for {TemplateKey} ({ErrorCode}): {Error}",
                    template.Key,
                    review.ErrorCode,
                    review.Error);
                MessageBox.Show(
                    I18n(WorkflowErrorPresentation.GetResourceKey(review.ErrorCode)),
                    I18n("workflow.template.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _reviewingTemplate = true;
            _importReview = review;
            RenderImportReview();
        }

        private void CancelImport_Click(object sender, RoutedEventArgs e)
        {
            _importReview = null;
            _reviewingTemplate = false;
            RenderEditor();
        }

        private void AdoptImport_Click(object sender, RoutedEventArgs e)
        {
            var review = _importReview;
            if (review is null) return;
            if (_session.State.IsDirty)
            {
                var sourceLabel = ImportSourceLabel();
                var answer = MessageBox.Show(
                    Format("workflow.import.confirm.replaceDraft", sourceLabel),
                    Format("workflow.import.confirm.title", sourceLabel),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            if (!_session.AdoptImport(review))
            {
                var sourceLabel = ImportSourceLabel();
                Log.Warning(
                    "Workflow import review could not be adopted ({ErrorCode}): {Error}",
                    _session.State.ErrorCode,
                    _session.State.Error);
                MessageBox.Show(
                    I18n(WorkflowErrorPresentation.GetResourceKey(_session.State.ErrorCode)),
                    Format("workflow.import.confirm.title", sourceLabel),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _importReview = null;
            _reviewingTemplate = false;
            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            ClearReportPresentation();
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
            if (_rendering
                || sender is not ComboBox
                {
                    DataContext: StepEditorItem item,
                    IsKeyboardFocusWithin: true,
                }) return;
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
                Title = I18n("workflow.export.dialog.title"),
                Filter = I18n("workflow.export.dialog.filter"),
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
                    ? Format("workflow.export.success", result.Path ?? string.Empty)
                    : I18n("workflow.export.error"),
                I18n("workflow.export.dialog.title"),
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void DuplicateWorkflow_Click(object sender, RoutedEventArgs e)
            => DuplicateCurrentWorkflow();

        internal bool DuplicateCurrentWorkflow()
        {
            var source = _session.State.Draft;
            if (source is null) return false;
            var suffix = $".copy-{Guid.NewGuid():N}";
            var prefixLength = Math.Min(source.Id.Length, 64 - suffix.Length);
            var copyId = source.Id[..prefixLength] + suffix;
            var copyNameSuffix = I18n("workflow.duplicate.copySuffix");
            var nameLength = Math.Min(source.Name.Length, 120 - copyNameSuffix.Length);
            var copyName = source.Name[..nameLength] + copyNameSuffix;
            if (!_session.DuplicateCurrent(copyId, copyName))
            {
                RenderStatus();
                return false;
            }

            _rendering = true;
            WorkflowList.SelectedItem = null;
            CompactWorkflowCombo.SelectedItem = null;
            _rendering = false;
            ClearReportPresentation();
            RenderEditor();
            WorkflowNameBox.Focus();
            WorkflowNameBox.SelectAll();
            return true;
        }

        private async void DeleteWorkflow_Click(object sender, RoutedEventArgs e)
        {
            var draft = _session.State.Draft;
            if (draft is null || _session.State.ExistingDefinitionSha256 is null) return;
            var answer = MessageBox.Show(
                Format("workflow.delete.confirm", draft.Name),
                I18n("workflow.delete.title"),
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
            ClearReportPresentation();
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
                return I18n("workflow.execution.notStarted.detail");

            await RefreshListAsync(workflowId);
            RenderEditor();
            await RefreshReportsAsync(workflowId);
            return PrepareRun(expectedStateFingerprint)
                ? null
                : _executionReview is not null
                    ? Format(
                        "workflow.execution.prepareFailed.detail",
                        _executionReview.Issues.Count)
                    : I18n("workflow.execution.notStarted.detail");
        }

        internal async Task<string?> OpenEditorAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
        {
            _runSession.CancelReview();
            _executionReview = null;
            ExecutionReviewPanel.Visibility = Visibility.Collapsed;
            if (!await _session.LoadAsync(workflowId, cancellationToken))
                return I18n("workflow.execution.notStarted.detail");

            await RefreshListAsync(workflowId);
            RenderEditor();
            await RefreshReportsAsync(workflowId);
            return null;
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
                var failure = WorkflowExecutionPresentation.DescribePrepareFailure(
                    _executionReview,
                    I18n);
                ExecutionResultTitle.Text = failure.Title;
                ExecutionResultDetail.Text = failure.Detail;
                ExecutionOutputList.ItemsSource = failure.Outputs;
                ExecutionOutputList.Visibility = failure.HasOutputs ? Visibility.Visible : Visibility.Collapsed;
                ExecutionResultPanel.Visibility = Visibility.Visible;
                return false;
            }
            var presentation = WorkflowExecutionPresentation.DescribeReview(
                _executionReview,
                state.Draft.FailureMode,
                I18n);
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
                _lastRunResult = result;
                var presentation = WorkflowExecutionPresentation.DescribeRunResult(
                    result,
                    I18n);
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
                Log.Warning(
                    exception,
                    "Workflow terminal output could not be copied.");
                MessageBox.Show(
                    I18n("workflow.terminal.copyError"),
                    I18n("workflow.terminal.copyErrorTitle"),
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
                Title = I18n("workflow.terminal.export.dialogTitle"),
                Filter = I18n("workflow.terminal.export.dialogFilter"),
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
                    I18n(WorkflowErrorPresentation.GetResourceKey(review.ErrorCode)),
                    I18n("workflow.terminal.export.prepareFailedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var approved = MessageBox.Show(
                Format(
                    "workflow.terminal.export.confirm",
                    item.Source.StepId,
                    item.Source.OutputKey,
                    OutputTypeLabel(item.Source.Type),
                    review.Utf8ByteCount,
                    review.ValueSha256,
                    review.DestinationPath ?? string.Empty),
                I18n("workflow.terminal.export.confirmTitle"),
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
                    ? Format(
                        "workflow.terminal.export.success",
                        result.Path ?? string.Empty,
                        result.ValueSha256 ?? string.Empty)
                    : TerminalExportFailureMessage(result.ErrorCode),
                I18n("workflow.terminal.export.resultTitle"),
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        internal bool ClearTerminalOutputs()
        {
            var hadOutputs = TerminalOutputPanel.Visibility == Visibility.Visible;
            TerminalOutputList.ItemsSource = null;
            TerminalOutputPanel.Visibility = Visibility.Collapsed;
            if (_lastRunResult?.Execution is { } execution
                && execution.TerminalOutputs.Count > 0)
            {
                _lastRunResult = _lastRunResult with
                {
                    Execution = execution with
                    {
                        TerminalOutputs = Array.Empty<WorkflowTerminalOutput>(),
                    },
                };
            }
            if (hadOutputs) TerminalOutputsCleared?.Invoke(this, EventArgs.Empty);
            return hadOutputs;
        }

        internal WorkflowTerminalOutput? GetTerminalOutputForQuality()
            => (TerminalOutputList.ItemsSource as IEnumerable<WorkflowTerminalOutputItem>)?
                .FirstOrDefault()?
                .Source;

        private async Task RefreshReportsAsync(string workflowId)
        {
            var version = Interlocked.Increment(ref _reportListVersion);
            var result = await _reports.ListAsync(workflowId);
            if (version != _reportListVersion) return;
            _selectedReport = null;
            if (!result.IsSuccess)
            {
                ReportDetailTitle.Text = I18n("workflow.reports.unavailable");
                ReportDetailMeta.Text = I18n(
                    WorkflowErrorPresentation.GetResourceKey(result.ErrorCode));
                return;
            }
            _reportSummaries = result.Reports;
            ReportList.ItemsSource = WorkflowExecutionPresentation.ToReportListItems(
                _reportSummaries,
                I18n);
            if (result.Issues.Count > 0)
                ReportDetailMeta.Text = Format(
                    "workflow.reports.loadIssues",
                    result.Issues.Count);
        }

        private async void ReportList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReportList.SelectedItem is not WorkflowReportListItem item) return;
            var version = Interlocked.Increment(ref _reportLoadVersion);
            var result = await _reports.LoadAsync(item.ReportId);
            if (version != _reportLoadVersion) return;
            if (!result.IsSuccess)
            {
                ReportDetailTitle.Text = I18n(
                    WorkflowErrorPresentation.GetResourceKey(result.ErrorCode));
                ReportDetailMeta.Text = string.Empty;
                ReportTimeline.ItemsSource = null;
                return;
            }
            _selectedReport = result.Report;
            var presentation = WorkflowExecutionPresentation.DescribeReport(
                result.Report!,
                I18n);
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
                var compensationOptions = new[] { new CommandOption(
                        string.Empty,
                        I18n("workflow.step.noCompensation")) }
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
                        EffectOptions = CreateEffectOptions(),
                        PrimaryInput = CreateInvocationEditor(
                            step.Id,
                            WorkflowCommandRole.Primary,
                            I18n("workflow.step.primaryInput"),
                            step.Command,
                            priorOutputs),
                        CompensationInput = CreateInvocationEditor(
                            step.Id,
                            WorkflowCommandRole.Compensation,
                            I18n("workflow.step.compensationInput"),
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
            ImportReviewHeading.Text = _reviewingTemplate
                ? I18n("workflow.import.reviewTemplate")
                : I18n("workflow.import.reviewExternal");
            AutomationProperties.SetName(
                ImportReviewPanel,
                ImportReviewHeading.Text);
            ImportReviewName.Text = $"{review.Workflow.Name}  ·  {review.Workflow.Id}";
            ImportReviewSource.Text = $"{review.SourcePath}\nSHA-256  {review.DefinitionSha256}";
            ImportReviewSummary.Text = Format(
                "workflow.import.summary",
                review.Workflow.Steps.Count,
                I18n(review.Workflow.FailureMode == WorkflowFailureMode.Compensate
                    ? "workflow.failure.rollbackShort"
                    : "workflow.failure.stopShort"),
                I18n(review.ContainsSensitiveInputs
                    ? "workflow.import.sensitive.present"
                    : "workflow.import.sensitive.none"));
            ImportReviewTrust.Text = review.TrustLevel switch
            {
                WorkflowDocumentTrustLevel.TrustedSource =>
                    I18n("workflow.import.trust.trusted"),
                WorkflowDocumentTrustLevel.LocalManaged =>
                    I18n("workflow.import.trust.local"),
                _ => I18n("workflow.import.trust.untrusted"),
            };
            var preflight = review.Preflight;
            ImportReviewPreflight.Text = preflight?.IsValid == true
                ? Format(
                    "workflow.import.preflight.passed",
                    preflight.Permissions.Count)
                : Format(
                    "workflow.import.preflight.failed",
                    preflight?.Issues.Count ?? 0);
        }

        private void SetImportReviewMode(bool active)
        {
            WorkflowList.IsEnabled = !active;
            CompactWorkflowCombo.IsEnabled = !active;
            RefreshWorkflowButton.IsEnabled = !active;
            ImportWorkflowButton.IsEnabled = !active;
            NewWorkflowButton.IsEnabled = !active;
            TemplateWorkflowButton.IsEnabled = !active;
            CompactRefreshButton.IsEnabled = !active;
            CompactImportButton.IsEnabled = !active;
            CompactTemplateButton.IsEnabled = !active;
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
                BindingEditor = new WorkflowBindingEditorModel(
                    availableOutputs,
                    invocation.InputType,
                    descriptor?.ArgumentSchema
                        .Select(declaration => declaration.Key)
                        .ToArray()),
                ArgumentSchema = SnapshotArgumentSchema(descriptor?.ArgumentSchema),
                ArgumentPresets = descriptor?.ArgumentPresets
                    .Select(preset => new WorkflowArgumentPresetOption(
                        preset.Id,
                        preset.Name,
                        new Dictionary<string, string>(
                            preset.Arguments,
                            StringComparer.Ordinal)))
                    .ToArray()
                    ?? Array.Empty<WorkflowArgumentPresetOption>(),
                InputType = invocation.InputType,
                InputOptions = options,
                Text = invocation.Text ?? string.Empty,
                Paths = invocation.Paths.ToArray(),
                ImagePng = invocation.ImagePng?.ToArray(),
            };
            editor.LoadArguments(invocation.Arguments);
            editor.BindingEditor.LoadBindings(command?.Bindings);
            editor.RefreshArgumentValidation();
            return editor;
        }

        private static IReadOnlyList<PluginCommandArgumentDeclaration> SnapshotArgumentSchema(
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema)
            => schema?.Select(declaration => new PluginCommandArgumentDeclaration
                {
                    Key = declaration.Key,
                    Name = declaration.Name,
                    Description = declaration.Description,
                    Type = declaration.Type,
                    Required = declaration.Required,
                    DefaultValue = declaration.DefaultValue,
                    Sensitive = declaration.Sensitive,
                    Minimum = declaration.Minimum,
                    Maximum = declaration.Maximum,
                    MinLength = declaration.MinLength,
                    MaxLength = declaration.MaxLength,
                    EnumValues = declaration.EnumValues.ToList(),
                })
                .ToArray()
                ?? Array.Empty<PluginCommandArgumentDeclaration>();

        private IReadOnlyList<WorkflowBindingOutputOption> GetDeclaredOutputOptions(
            string stepId,
            WorkflowCommand? command)
        {
            var descriptor = command is null ? null : _plugins.Commands.Get(command.CommandKey);
            return descriptor?.Outputs.Select(output => new WorkflowBindingOutputOption(
                    stepId,
                    output.Key,
                    output.Type,
                    output.Description))
                .ToArray()
                ?? Array.Empty<WorkflowBindingOutputOption>();
        }

        private bool ApplyInvocation(WorkflowInvocationEditorModel item)
        {
            if (!item.BindingEditor.TryBuildBindings(out var bindings)
                || !item.TryBuildArguments(out var arguments)) return false;
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
                ? I18n("workflow.status.notSaved")
                : state.IsDirty
                    ? I18n("workflow.status.unsavedChanges")
                    : I18n("workflow.status.savedLocally");
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
                PreflightTitle.Text = I18n("workflow.preflight.invalidInput");
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Danger");
                PreflightDetail.Text = I18n("workflow.preflight.invalidInputDetail");
                return;
            }
            if (state.Preflight?.IsValid == true)
            {
                PreflightTitle.Text = I18n("workflow.preflight.passed");
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Success");
                PreflightDetail.Text = state.Preflight.Permissions.Count == 0
                    ? I18n("workflow.preflight.noPermissions")
                    : string.Format(
                        I18n("workflow.preflight.permissionCount"),
                        state.Preflight.Permissions.Count);
            }
            else
            {
                PreflightTitle.Text = I18n("workflow.preflight.needsFix");
                PreflightTitle.Foreground = (System.Windows.Media.Brush)FindResource("Long.Brush.State.Danger");
                var errorCode = state.ErrorCode != WorkflowErrorCode.None
                    ? state.ErrorCode
                    : state.Preflight?.ErrorCode ?? WorkflowErrorCode.PreflightDefinitionInvalid;
                PreflightDetail.Text = I18n(
                    WorkflowErrorPresentation.GetResourceKey(errorCode));
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
            Grid.SetRow(EditorHeaderActions, compact ? 1 : 0);
            Grid.SetColumn(EditorHeaderActions, compact ? 0 : 1);
            Grid.SetColumnSpan(EditorHeaderActions, compact ? 2 : 1);
            EditorHeaderActions.Margin = compact
                ? new Thickness(0, 10, 0, 0)
                : new Thickness(0);
            AutomationProperties.SetItemStatus(
                this,
                $"layout:{(compact ? "compact" : "wide")};width:{Math.Round(width)}");
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
            DuplicateWorkflowButton.IsEnabled = enabled && _session.State.Draft is not null;
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

        private static string InputTypeLabel(AcceptedInputType inputType)
            => I18n(inputType switch
            {
                AcceptedInputType.None => "workflow.input.none",
                AcceptedInputType.Text => "workflow.input.text",
                AcceptedInputType.Url => "workflow.input.url",
                AcceptedInputType.Image => "workflow.input.image",
                AcceptedInputType.File => "workflow.input.file",
                AcceptedInputType.Files => "workflow.input.files",
                AcceptedInputType.Folder => "workflow.input.folder",
                AcceptedInputType.Clipboard => "workflow.input.clipboard",
                _ => "workflow.input.explorerSelection",
            });

        private static string OutputTypeLabel(PluginCommandOutputType outputType)
            => I18n(outputType == PluginCommandOutputType.Path
                ? "workflow.execution.output.type.path"
                : "workflow.execution.output.type.text");

        private static string TerminalExportFailureMessage(
            WorkflowErrorCode errorCode)
            => I18n(WorkflowErrorPresentation.GetResourceKey(errorCode));

        private void RefreshLocalizedExecutionState()
        {
            if (_executionReview is not null)
            {
                if (_executionReview.IsValid
                    && _session.State.Draft is { } draft)
                {
                    var review = WorkflowExecutionPresentation.DescribeReview(
                        _executionReview,
                        draft.FailureMode,
                        I18n);
                    ExecutionReviewSummary.Text = review.Summary;
                    ExecutionPermissionList.ItemsSource = review.Permissions;
                }
                else
                {
                    var failure = WorkflowExecutionPresentation.DescribePrepareFailure(
                        _executionReview,
                        I18n);
                    ApplyExecutionResult(failure);
                }
            }
            else if (_lastRunResult is not null)
            {
                ApplyExecutionResult(WorkflowExecutionPresentation.DescribeRunResult(
                    _lastRunResult,
                    I18n));
            }
        }

        private void ApplyExecutionResult(
            WorkflowExecutionResultPresentation presentation)
        {
            ExecutionResultTitle.Text = presentation.Title;
            ExecutionResultDetail.Text = presentation.Detail;
            ExecutionOutputList.ItemsSource = presentation.Outputs;
            ExecutionOutputList.Visibility = presentation.HasOutputs
                ? Visibility.Visible
                : Visibility.Collapsed;
            TerminalOutputList.ItemsSource = presentation.TerminalOutputs;
            TerminalOutputPanel.Visibility = presentation.HasTerminalOutputs
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void RefreshLocalizedReports()
        {
            ReportList.ItemsSource = WorkflowExecutionPresentation.ToReportListItems(
                _reportSummaries,
                I18n);
            if (_selectedReport is null) return;
            var presentation = WorkflowExecutionPresentation.DescribeReport(
                _selectedReport,
                I18n);
            ReportDetailTitle.Text = presentation.Title;
            ReportDetailMeta.Text = presentation.Meta;
            ReportTimeline.ItemsSource = presentation.Timeline;
        }

        private void ClearReportPresentation()
        {
            _reportSummaries = Array.Empty<WorkflowExecutionReportSummary>();
            _selectedReport = null;
            ReportList.ItemsSource = null;
            ReportTimeline.ItemsSource = null;
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

        private static string Format(string key, params object[] arguments)
            => string.Format(I18n(key), arguments);

        private string ImportSourceLabel()
            => I18n(_reviewingTemplate
                ? "workflow.import.source.template"
                : "workflow.import.source.external");

        private sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;
        private sealed record CommandOption(string Key, string Display)
        {
            public static CommandOption From(CommandDescriptor descriptor)
                => new(descriptor.Key, $"{descriptor.Title} · {descriptor.PluginName}");
        }
        private sealed record WorkflowListItem(string Id, string Name, string Detail)
        {
            public static WorkflowListItem From(ManagedCommandWorkflowSummary summary)
                => new(
                    summary.Id,
                    summary.Name,
                    string.Format(
                        I18n("workflow.list.detail"),
                        summary.StepCount,
                        I18n(summary.FailureMode == WorkflowFailureMode.Compensate
                            ? "workflow.failure.rollbackShort"
                            : "workflow.failure.stopShort")));
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
