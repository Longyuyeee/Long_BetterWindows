using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LongBetterWindows.Host.Automation;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class SuperPanelWindow : Window
    {
        private static SuperPanelWindow? _instance;
        private readonly PluginRegistry _plugins;
        private readonly SuperPanelActionCoordinator _actionCoordinator;
        private readonly SuperPanelGroupCoordinator _groupCoordinator;
        private readonly SuperPanelGroupEditorSession _groupEditor;
        private readonly SuperPanelSearchSession _searchSession;
        private readonly SuperPanelDragSession _dragSession = new();
        private readonly SuperPanelWindowLifecycle _windowLifecycle;
        private readonly QualityWindowAutomation? _qualityAutomation;

        private SuperPanelWindow()
        {
            InitializeComponent();
            _windowLifecycle = new SuperPanelWindowLifecycle(
                this, PanelChrome, CycleGroup);
            _plugins = HostProvider.Instance.PluginStore;
            _actionCoordinator = new SuperPanelActionCoordinator(
                _plugins,
                ServicesInitializer.SearchPreferences,
                WorkflowReviewNavigation.OpenAsync,
                key => ServicesInitializer.I18n.T(key),
                WorkspaceModuleNavigation.OpenAsync);
            _groupCoordinator = new SuperPanelGroupCoordinator(
                ServicesInitializer.SearchPreferences,
                ServicesInitializer.SuperPanelGroups,
                key => ServicesInitializer.I18n.T(key));
            _groupEditor = new SuperPanelGroupEditorSession(_groupCoordinator);
            _searchSession = new SuperPanelSearchSession(
                ServicesInitializer.ContextCapture,
                ServicesInitializer.Search,
                ServicesInitializer.SuperPanelGroups);
            _searchSession.ContextUpdated += SearchSession_ContextUpdated;
            _searchSession.ResultsUpdated += SearchSession_ResultsUpdated;
            RenderActiveGroup();
            _plugins.PluginsChanged += OnPluginsChanged;
            _qualityAutomation = QualityWindowAutomation.Attach(
                this,
                ExecuteQualityWindowAction);
            Closed += (_, _) =>
            {
                _plugins.PluginsChanged -= OnPluginsChanged;
                _searchSession.ContextUpdated -= SearchSession_ContextUpdated;
                _searchSession.ResultsUpdated -= SearchSession_ResultsUpdated;
                _searchSession.Dispose();
                _windowLifecycle.Dispose();
                _qualityAutomation?.Dispose();
                _instance = null;
            };
        }

        public static void ShowPanel()
            => ShowPanelCore(null);

        internal static void ShowPanelForQuality(bool useEmptyContext = false)
            => ShowPanelCore(useEmptyContext
                ? ContextSnapshot.Empty
                : new ContextSnapshot(DateTimeOffset.UtcNow, new[]
                {
                    new ContextItem
                    {
                        Id = "quality.url",
                        Source = ContextSource.Clipboard,
                        Label = string.Format(
                            ServicesInitializer.I18n.T(
                                "superPanel.quality.clipboardLink"),
                            "https://long.example/quality"),
                        Text = "https://long.example/quality",
                        CompatibleInputTypes = new[]
                        {
                            LongBetterWindows.Host.Contracts.AcceptedInputType.Url,
                            LongBetterWindows.Host.Contracts.AcceptedInputType.Clipboard,
                            LongBetterWindows.Host.Contracts.AcceptedInputType.Text,
                        },
                    },
                }));

        private static void ShowPanelCore(ContextSnapshot? presetContext)
        {
            var started = Stopwatch.GetTimestamp();
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ShowPanelCore(presetContext));
                return;
            }

            var foreground = Shell32.GetForegroundWindow();
            var request = new ContextCaptureRequest(foreground, DateTimeOffset.UtcNow);
            _instance ??= new SuperPanelWindow();
            _instance._windowLifecycle.CaptureForegroundWindow(foreground);
            if (presetContext is null)
                _instance.BeginLoad(request);
            else
                _instance.ApplyContext(presetContext);

            _instance._windowLifecycle.Present(animate: true);
            Log.Debug("Super Panel visible: {ElapsedMs:F1}ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        private void BeginLoad(ContextCaptureRequest request)
            => _ = _searchSession.StartCaptureAsync(request);

        private void ApplyContext(ContextSnapshot snapshot)
            => _ = _searchSession.StartWithContextAsync(snapshot);

        private void SearchSession_ContextUpdated(
            object? sender,
            SuperPanelContextUpdate update)
        {
            _groupCoordinator.ResetResults();
            var view = SuperPanelViewProjection.ProjectContext(
                update,
                key => ServicesInitializer.I18n.T(key));
            ContextBadges.ItemsSource = view.Items;
            ContextBadges.Visibility = view.ShowBadges
                ? Visibility.Visible
                : Visibility.Collapsed;
            ContextSummary.Text = view.Summary;
            RenderActiveGroup();
        }

        private void SearchSession_ResultsUpdated(
            object? sender,
            SuperPanelResultsUpdate update)
        {
            _groupCoordinator.SetResults(update.Results, update.Completed);
            RenderActiveGroup();
        }
        private void RenderActiveGroup()
        {
            var view = _groupCoordinator.BuildView();
            ResultsList.ItemsSource = view.VisibleResults;
            ResultsList.SelectedIndex = view.VisibleResults.Count > 0 ? 0 : -1;
            EmptyState.Visibility = view.ShowEmptyState
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyStateText.Text = view.EmptyStateText;
            StatusText.Text = view.StatusText;
            InteractionHint.Text = view.InteractionHint;
            CustomGroupActions.Visibility = view.ShowCustomGroupActions
                ? Visibility.Visible
                : Visibility.Collapsed;
            GroupTabs.ItemsSource = view.Groups;
        }
        private async Task ExecuteAsync(SearchResultItem selected)
        {
            var outcome = await _actionCoordinator.ExecuteAsync(
                selected,
                selected.PrimaryAction,
                _searchSession.CurrentContext,
                beforeCommandExecution: async () =>
                {
                    Hide();
                    await Task.Delay(35);
                });
            ApplyActionOutcome(outcome);
        }

        private void SecondaryActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SearchResultItem selected } button
                || !selected.HasSecondaryActions) return;

            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = button };
            foreach (var projection in SearchResultActionMenuProjection.Build(selected))
            {
                var item = new MenuItem
                {
                    Header = projection.Header,
                    Tag = projection.Action,
                };
                AutomationProperties.SetAutomationId(
                    item, projection.AutomationId);
                item.Click += async (_, _) =>
                    await ExecuteSecondaryActionAsync(selected, projection.Action);
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private async Task ExecuteSecondaryActionAsync(
            SearchResultItem selected,
            SearchResultAction action)
        {
            var outcome = await _actionCoordinator.ExecuteAsync(
                selected, action, _searchSession.CurrentContext);
            ApplyActionOutcome(outcome);
        }

        private void ApplyActionOutcome(SuperPanelActionOutcome outcome)
        {
            var view = SuperPanelViewProjection.ProjectAction(outcome);
            if (view.Disposition == SuperPanelActionDisposition.ContinueSearch)
            {
                Hide();
                CommandPaletteWindow.ShowPalette(view.ContinuationQuery ?? string.Empty);
                return;
            }

            StatusText.Text = view.Status;
            if (view.Disposition == SuperPanelActionDisposition.Hide)
            {
                Hide();
                return;
            }

            _windowLifecycle.Present(animate: false);
        }

        private void OnPluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(RefreshSearch);
                return;
            }
            RefreshSearch();
        }

        private void RefreshSearch() => _ = _searchSession.RefreshSearchAsync();

        private async void ResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragSession.ConsumeClickSuppression())
                return;
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;
            if (ResultsList.SelectedItem is SearchResultItem selected)
                await ExecuteAsync(selected);
        }

        private async void PinResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string resultId }) return;
            e.Handled = true;
            await _groupCoordinator.TogglePinnedAsync(resultId);
            await _searchSession.RefreshSearchAsync();
        }

        private void GroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string groupId })
                SwitchGroup(groupId);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowLifecycle.AttachWindowMessageHook();
        }

        private void CycleGroup(int wheelDelta)
        {
            if (_groupCoordinator.Cycle(wheelDelta))
                CompleteGroupSwitch();
        }

        private void SwitchGroup(string groupId)
        {
            if (_groupCoordinator.SelectGroup(groupId))
                CompleteGroupSwitch();
        }

        private void CompleteGroupSwitch()
        {
            _dragSession.Reset();
            RenderActiveGroup();
        }

        private void ResultsList_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is SearchResultItem result)
                _dragSession.TryBegin(
                    _groupCoordinator.ActiveGroupId,
                    result,
                    e.GetPosition(ResultsList),
                    FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null);
        }

        private void ResultsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragSession.TryStartDrag(
                    e.GetPosition(ResultsList),
                    e.LeftButton,
                    SystemParameters.MinimumHorizontalDragDistance,
                    SystemParameters.MinimumVerticalDragDistance,
                    out var resultId))
                return;
            DragDrop.DoDragDrop(ResultsList, resultId!, DragDropEffects.Move);
        }

        private void ResultsList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = SuperPanelDragSession.CanDropOnResults(
                            _groupCoordinator.ActiveGroupId)
                        && e.Data.GetDataPresent(typeof(string))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void ResultsList_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetData(typeof(string)) is not string resultId)
                return;
            var targetContainer = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            var visible = (ResultsList.ItemsSource as IEnumerable<SearchResultItem>)?.ToList()
                ?? new List<SearchResultItem>();
            var targetIndex = targetContainer?.DataContext is SearchResultItem target
                ? visible.FindIndex(item => string.Equals(
                    item.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                : visible.Count - 1;
            var outcome = await _groupCoordinator.ReorderActiveResultAsync(
                resultId, targetIndex);
            if (outcome.Success)
            {
                _dragSession.CompleteDrop();
                RenderActiveGroup();
                StatusText.Text = outcome.Message;
            }
        }

        private void GroupButton_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = sender is Button { Tag: string groupId }
                        && SuperPanelDragSession.CanDropOnGroup(groupId)
                        && e.Data.GetDataPresent(typeof(string))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void GroupButton_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { Tag: string groupId }
                || !SuperPanelDragSession.CanDropOnGroup(groupId)
                || e.Data.GetData(typeof(string)) is not string resultId)
                return;
            var outcome = await _groupCoordinator.MoveResultToGroupAsync(
                _dragSession.SourceGroupId, groupId, resultId);
            if (outcome.Success)
            {
                _dragSession.CompleteDrop();
                CompleteGroupSwitch();
                StatusText.Text = outcome.Message;
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
            => OpenGroupEditor(null, string.Empty);

        private void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = _groupCoordinator.ActiveCustomGroup;
            if (group is not null) OpenGroupEditor(group.Id, group.Title);
        }

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = _groupCoordinator.ActiveCustomGroup;
            if (group is null) return;
            var approved = ThemedMessageDialog.ShowConfirmation(
                this,
                string.Format(
                    I18n("superPanel.confirm.deleteGroup.message"),
                    group.Title),
                I18n("superPanel.confirm.deleteGroup.title"),
                ThemedMessageDialogTone.Danger);
            if (!approved) return;
            var outcome = await _groupCoordinator.DeleteActiveGroupAsync();
            if (outcome.Success)
            {
                RenderActiveGroup();
                StatusText.Text = outcome.Message;
            }
        }

        private void OpenGroupEditor(string? groupId, string title)
        {
            _groupEditor.Open(groupId, title);
            ApplyGroupEditorState(focusEditor: true);
        }

        private void ApplyGroupEditorState(bool focusEditor)
        {
            var state = _groupEditor.State;
            GroupEditorTitle.Text = state.Heading;
            GroupNameTextBox.Text = state.Title;
            GroupEditorPopup.IsOpen = state.IsOpen;
            if (!state.IsOpen || !focusEditor) return;
            Dispatcher.BeginInvoke(() =>
            {
                GroupNameTextBox.Focus();
                GroupNameTextBox.SelectAll();
            });
        }

        private async void SaveGroupEditor_Click(object sender, RoutedEventArgs e)
            => await SaveGroupEditorAsync();

        private async Task SaveGroupEditorAsync()
        {
            var outcome = await _groupEditor.SaveAsync(GroupNameTextBox.Text);
            if (!outcome.Success)
            {
                StatusText.Text = outcome.Message;
                return;
            }
            ApplyGroupEditorState(focusEditor: false);
            RenderActiveGroup();
            StatusText.Text = outcome.Message;
        }

        private void CancelGroupEditor_Click(object sender, RoutedEventArgs e)
        {
            _groupEditor.Cancel();
            ApplyGroupEditorState(focusEditor: false);
        }

        private async void GroupNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SaveGroupEditorAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _groupEditor.Cancel();
                ApplyGroupEditorState(focusEditor: false);
                e.Handled = true;
            }
        }

        private void OpenCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            CommandPaletteWindow.ShowPalette();
        }

        private void Close_Click(object sender, RoutedEventArgs e) =>
            _windowLifecycle.Dismiss(restoreFocus: true);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var selected = ResultsList.SelectedItem as SearchResultItem;
            var command = SuperPanelKeyboardRouter.Resolve(
                e.Key,
                Keyboard.Modifiers,
                selected,
                _groupCoordinator.ActiveGroupId);
            switch (command)
            {
                case SuperPanelKeyboardCommand.ExecuteSecondary:
                    _ = ExecuteSecondaryActionAsync(selected!, selected!.SecondaryActions[0]);
                    break;
                case SuperPanelKeyboardCommand.RemoveFromGroup:
                    _ = RemoveFromActiveGroupAsync(selected!.Id);
                    break;
                case SuperPanelKeyboardCommand.ExecutePrimary:
                    _ = ExecuteAsync(selected!);
                    break;
                case SuperPanelKeyboardCommand.Dismiss:
                    _windowLifecycle.Dismiss(restoreFocus: true);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private bool ExecuteQualityWindowAction(QualityWindowAction action)
        {
            var selected = ResultsList.SelectedItem as SearchResultItem;
            switch (action)
            {
                case QualityWindowAction.ExecutePrimary when selected is not null:
                    _ = ExecuteAsync(selected);
                    return true;
                case QualityWindowAction.ExecuteSecondary
                    when selected?.SecondaryActions.Count > 0:
                    _ = ExecuteSecondaryActionAsync(
                        selected,
                        selected.SecondaryActions[0]);
                    return true;
                case QualityWindowAction.Dismiss:
                    _windowLifecycle.Dismiss(restoreFocus: true);
                    return true;
                case QualityWindowAction.SelectDeterministicResult
                    when ResultsList.Items.Count > 0:
                    ResultsList.SelectedIndex = 0;
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                    return ResultsList.SelectedItem is SearchResultItem;
                default:
                    return false;
            }
        }

        private async Task RemoveFromActiveGroupAsync(string resultId)
        {
            var outcome = await _groupCoordinator.RemoveFromActiveGroupAsync(resultId);
            if (outcome.Success)
            {
                RenderActiveGroup();
                StatusText.Text = outcome.Message;
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            _windowLifecycle.HandleDeactivated(App.KeepSuperPanelVisibleForQuality);
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source is not null)
            {
                if (source is T match) return match;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);

    }
}
