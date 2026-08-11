using System.Windows;
using System.Windows.Automation;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LongBetterWindows.Host.Automation;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class CommandPaletteWindow : Window
    {
        private static CommandPaletteWindow? _instance;
        private readonly PluginRegistry _plugins;
        private readonly CommandExecutor _executor;
        private readonly SearchResultActionExecutor _actionExecutor;
        private ContextSnapshot _contextSnapshot = ContextSnapshot.Empty;
        private nint _originWindowHandle;
        private CancellationTokenSource? _contextCts;
        private CancellationTokenSource? _searchCts;
        private string? _preferredSelectionId;
        private readonly QualityWindowAutomation? _qualityAutomation;
        private readonly LauncherLatencySession _latency = new();
        private EventHandler? _firstFrameHandler;
        private bool _suppressSearch;

        private CommandPaletteWindow()
        {
            InitializeComponent();
            _plugins = HostProvider.Instance.PluginStore;
            _executor = new CommandExecutor(_plugins);
            _actionExecutor = new SearchResultActionExecutor(
                _plugins,
                WorkflowReviewNavigation.OpenAsync,
                key => ServicesInitializer.I18n.T(key),
                WorkspaceModuleNavigation.OpenAsync);
            _plugins.PluginsChanged += OnPluginsChanged;
            _qualityAutomation = QualityWindowAutomation.Attach(
                this,
                ExecuteQualityWindowAction);
            Closed += (_, _) =>
            {
                _plugins.PluginsChanged -= OnPluginsChanged;
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _contextCts?.Cancel();
                _contextCts?.Dispose();
                _qualityAutomation?.Dispose();
                DetachFirstFrameHandler();
                _instance = null;
            };
        }

        public static void ShowPalette()
            => ShowPalette(initialQuery: null);

        public static void ShowPalette(string? initialQuery)
        {
            var started = Stopwatch.GetTimestamp();
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ShowPaletteCore(initialQuery, started));
                return;
            }

            ShowPaletteCore(initialQuery, started);
        }

        private static void ShowPaletteCore(string? initialQuery, long started)
        {

            var captureRequest = new ContextCaptureRequest(
                Shell32.GetForegroundWindow(),
                DateTimeOffset.UtcNow);
            _instance ??= new CommandPaletteWindow();
            _instance.BeginPresentationLatency(started);
            ServicesInitializer.LauncherContinuity.Discard();
            _instance._originWindowHandle = captureRequest.ForegroundWindowHandle;
            _instance._preferredSelectionId = null;
            _instance._suppressSearch = true;
            try
            {
                _instance.SearchBox.Text = initialQuery ?? string.Empty;
            }
            finally
            {
                _instance._suppressSearch = false;
            }
            _instance.SearchBox.CaretIndex = _instance.SearchBox.Text.Length;
            _instance.StatusText.Text = string.Empty;

            if (!_instance.IsVisible)
                _instance.Show();

            _instance.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => _instance.BeginLoadContext(captureRequest));
            _instance.Activate();
            _instance.SearchBox.Focus();
            _instance.AnimateIn();
            Log.Debug("Command Palette 可输入: {ElapsedMs:F1}ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        internal static void ShowPalette(PanelExpansionIntent intent)
        {
            ArgumentNullException.ThrowIfNull(intent);
            var started = Stopwatch.GetTimestamp();
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ShowPaletteCore(intent, started));
                return;
            }

            ShowPaletteCore(intent, started);
        }

        private static void ShowPaletteCore(PanelExpansionIntent intent, long started)
        {

            var state = intent.Consume();
            _instance ??= new CommandPaletteWindow();
            _instance.BeginPresentationLatency(started);
            ServicesInitializer.LauncherContinuity.Discard();
            _instance._contextCts?.Cancel();
            _instance._originWindowHandle = state.OriginWindowHandle;
            _instance._contextSnapshot = state.Context;
            _instance._preferredSelectionId = state.SelectedResultId;
            _instance._suppressSearch = true;
            try
            {
                _instance.SearchBox.Text = state.Query;
            }
            finally
            {
                _instance._suppressSearch = false;
            }
            _instance.SearchBox.CaretIndex = state.Query.Length;
            _instance.RenderContextBadges(beginSearch: false);
            _instance.StatusText.Text = string.Empty;

            if (!_instance.IsVisible)
                _instance.Show();
            _instance.Activate();
            _instance.SearchBox.Focus();
            _instance.AnimateIn();
            _instance.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => _instance.BeginSearch());
        }

        internal static void RestoreFromWorkspace(LauncherReturnState state)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => RestoreFromWorkspace(state));
                return;
            }

            _instance ??= new CommandPaletteWindow();
            _instance._originWindowHandle = state.OriginWindowHandle;
            _instance._contextSnapshot = state.Context;
            _instance._preferredSelectionId = null;
            _instance.SearchBox.Text = state.Query;
            _instance.SearchBox.CaretIndex = state.Query.Length;
            _instance.RenderContextBadges();
            _instance.StatusText.Text = string.Empty;
            if (!_instance.IsVisible)
                _instance.Show();
            _instance.Activate();
            _instance.SearchBox.Focus();
            _instance.AnimateIn();
        }

        private void AnimateIn()
        {
            var duration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
                ? token.TimeSpan
                : TimeSpan.FromMilliseconds(180);

            Opacity = duration == TimeSpan.Zero ? 1 : 0;
            var translate = new TranslateTransform(0, duration == TimeSpan.Zero ? 0 : -8);
            PaletteChrome.RenderTransform = translate;
            if (duration == TimeSpan.Zero) return;

            BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(0, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        private void OnPluginsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(BeginSearch);
                return;
            }

            BeginSearch();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (_suppressSearch) return;
            BeginSearch(debounce: true);
        }

        private void BeginSearch(bool debounce = false)
        {
            _latency.BeginQuery();
            UpdateLatencyAutomationStatus();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            _ = RunSearchAsync(debounce, _searchCts.Token);
        }

        private async Task RunSearchAsync(bool debounce, CancellationToken token)
        {
            try
            {
                if (debounce)
                    await Task.Delay(25, token);

                StatusText.Foreground = (Brush)FindResource("Long.Brush.Text.Secondary");
                StatusText.Text = I18n("palette.status.searching");
                var request = new SearchRequest(
                    SearchBox.Text,
                    _contextSnapshot,
                    MaxResults: 12,
                    AdditionalPreferredResultIds: _preferredSelectionId is null
                        ? null
                        : new[] { _preferredSelectionId });
                var finalResults = await ServicesInitializer.Search.SearchIncrementalAsync(
                    request,
                    results =>
                    {
                        if (!token.IsCancellationRequested)
                            ApplyResults(results, completed: false);
                        return Task.CompletedTask;
                    },
                    token,
                    metrics => Log.Debug(
                        "Search completed: FirstBatchMs={FirstBatchMs:F1}, TotalMs={TotalMs:F1}, Providers={ProviderCount}, Batches={BatchCount}, Results={ResultCount}",
                        metrics.FirstBatchElapsed?.TotalMilliseconds,
                        metrics.TotalElapsed.TotalMilliseconds,
                        metrics.ProviderCount,
                        metrics.BatchCount,
                        metrics.ResultCount));
                token.ThrowIfCancellationRequested();
                ApplyResults(finalResults, completed: true);
            }
            catch (OperationCanceledException)
            {
                // A newer query superseded this one.
            }
        }

        private void BeginLoadContext(ContextCaptureRequest request)
        {
            _contextCts?.Cancel();
            _contextCts?.Dispose();
            _contextCts = new CancellationTokenSource();
            _contextSnapshot = ContextSnapshot.Empty;
            RenderContextBadges();
            _ = LoadContextAsync(request, _contextCts.Token);
        }

        private async Task LoadContextAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var snapshot = await ServicesInitializer.ContextCapture.CaptureAsync(
                    request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _contextSnapshot = snapshot;
                RenderContextBadges();
            }
            catch (OperationCanceledException)
            {
                // A newer invocation superseded this capture.
            }
        }

        private void RenderContextBadges(bool beginSearch = true)
        {
            ContextBadges.ItemsSource = _contextSnapshot.Items;
            ContextBadges.Visibility = _contextSnapshot.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (beginSearch)
                BeginSearch();
        }

        private void ContextRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string itemId }) return;
            _contextSnapshot = _contextSnapshot.Without(itemId);
            RenderContextBadges();
        }

        private void ApplyResults(
            IReadOnlyList<SearchResultItem> results,
            bool completed)
        {
            if (!IsInitialized) return;

            var selectedId = _preferredSelectionId
                ?? (ResultsList.SelectedItem as LauncherResultViewItem)?.Id;
            var projected = LauncherResultProjection.Build(
                results,
                SearchBox.Text,
                _contextSnapshot,
                ServicesInitializer.SearchPreferences.GetRecentResultIds(),
                key => ServicesInitializer.I18n.T(key));
            var view = CollectionViewSource.GetDefaultView(projected);
            if (view is ListCollectionView listView)
            {
                listView.GroupDescriptions.Add(
                    new PropertyGroupDescription(
                        nameof(LauncherResultViewItem.SectionTitle)));
                listView.Refresh();
            }
            ResultsList.ItemsSource = view;
            var selection = StableSelectionResolver.Resolve(
                projected,
                selectedId,
                item => item.Id);
            var selectedItem = selection.Index >= 0
                ? projected[selection.Index]
                : null;
            if (selectedItem is not null)
                view.MoveCurrentTo(selectedItem);
            ResultsList.SelectedItem = selectedItem;
            if (_preferredSelectionId is not null && completed)
                _preferredSelectionId = null;
            EmptyState.Visibility = completed && projected.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyStateText.Text = _plugins.Commands.Count == 0
                ? I18n("palette.status.pluginsLoading")
                : I18n("palette.empty");
            StatusText.Text = projected.Count == 0
                ? completed
                    ? I18n("palette.empty")
                    : I18n("palette.status.searching")
                : string.Format(
                    I18n("palette.status.resultCount"),
                    projected.Count);
            var resultLatency = _latency.MarkFirstActionableResults(projected.Count);
            if (resultLatency is not null)
            {
                UpdateLatencyAutomationStatus();
                Log.Debug(
                    "Command Palette actionable results: InvocationMs={InvocationMs:F1}, QueryMs={QueryMs:F1}, Results={ResultCount}",
                    resultLatency.InvocationElapsed?.TotalMilliseconds,
                    resultLatency.QueryElapsed.TotalMilliseconds,
                    projected.Count);
            }
        }

        private async Task ExecuteSelectedAsync()
        {
            if (ResultsList.SelectedItem is not LauncherResultViewItem viewItem)
                return;
            var selected = viewItem.Result;

            if (selected.PrimaryAction.Kind == SearchActionKind.ContinueSearch)
            {
                SearchBox.Text = selected.PrimaryAction.Target;
                SearchBox.CaretIndex = SearchBox.Text.Length;
                SearchBox.Focus();
                return;
            }

            if (selected.PrimaryAction.Kind != SearchActionKind.ExecuteCommand)
            {
                await ExecuteHostActionAsync(selected, selected.PrimaryAction);
                return;
            }

            var descriptor = _plugins.Commands.Get(selected.PrimaryAction.Target);
            if (descriptor is null)
            {
                StatusText.Text = I18n("palette.status.resultExpired");
                return;
            }

            StatusText.Foreground = (Brush)FindResource("Long.Brush.Text.Secondary");
            StatusText.Text = I18n("palette.status.executing");
            var invocation = selected.PrimaryAction.Invocation;
            if (invocation is null)
                invocation = CommandInvocationFactory.Create(descriptor, _contextSnapshot);

            // 先归还前台焦点，再执行依赖“当前窗口/Explorer”的命令。
            Hide();
            if (_originWindowHandle != nint.Zero)
                ForegroundWindowActivator.TryActivate(_originWindowHandle);
            await Task.Delay(40);
            var result = await _executor.ExecuteAsync(
                descriptor.Key,
                invocation);

            if (result.IsSuccess)
                await ServicesInitializer.SearchPreferences.RecordUseAsync(selected.Id);

            if (result.IsSuccess && !result.KeepPaletteOpen)
                return;

            Show();
            Activate();
            StatusText.Text =
                result.Message ?? I18n("palette.status.executionFailed");
            StatusText.Foreground = (Brush)FindResource(result.IsSuccess
                ? "Long.Brush.State.Success"
                : "Long.Brush.State.Danger");
            SearchBox.Focus();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ExecuteSelectedAsync();
            }
        }

        private void MoveSelection(int offset)
        {
            if (ResultsList.Items.Count == 0) return;
            _preferredSelectionId = null;
            var next = Math.Clamp(
                ResultsList.SelectedIndex + offset,
                0,
                ResultsList.Items.Count - 1);
            ResultsList.SelectedIndex = next;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            AnnounceSelection();
        }

        private void AnnounceSelection()
        {
            if (ResultsList.SelectedItem is not LauncherResultViewItem selected)
                return;
            AccessibilityLiveRegion.Announce(
                SelectionAnnouncement,
                $"{selected.Title}, {ResultsList.SelectedIndex + 1}/{ResultsList.Items.Count}");
        }

        private void BeginPresentationLatency(long started)
        {
            _latency.BeginInvocation(started);
            UpdateLatencyAutomationStatus();
            DetachFirstFrameHandler();
            _firstFrameHandler = (_, _) =>
            {
                DetachFirstFrameHandler();
                var elapsed = _latency.MarkFirstFrame();
                if (elapsed is null) return;
                UpdateLatencyAutomationStatus();
                Log.Debug(
                    "Command Palette first rendered frame: {ElapsedMs:F1}ms",
                    elapsed.Value.TotalMilliseconds);
            };
            CompositionTarget.Rendering += _firstFrameHandler;
        }

        private void UpdateLatencyAutomationStatus()
            => AutomationProperties.SetItemStatus(this, _latency.ToAutomationStatus());

        private void DetachFirstFrameHandler()
        {
            if (_firstFrameHandler is null) return;
            CompositionTarget.Rendering -= _firstFrameHandler;
            _firstFrameHandler = null;
        }

        private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => await ExecuteSelectedAsync();

        private async void PinResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string resultId }) return;
            e.Handled = true;
            var pinned = await ServicesInitializer.SearchPreferences.TogglePinnedAsync(resultId);
            StatusText.Text = pinned
                ? I18n("palette.status.pinned")
                : I18n("palette.status.unpinned");
            BeginSearch();
        }

        private void SecondaryActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button
                {
                    DataContext: LauncherResultViewItem { Result: var selected },
                } button
                || !selected.HasSecondaryActions) return;

            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = button };
            for (var index = 0; index < selected.SecondaryActions.Count; index++)
            {
                var action = selected.SecondaryActions[index];
                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(action.Label)
                        ? I18n("palette.action.execute")
                        : action.Label,
                    Tag = action,
                };
                AutomationProperties.SetAutomationId(
                    item, $"Long.Result.SecondaryAction.{index}");
                item.Click += async (_, _) => await ExecuteHostActionAsync(selected, action);
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private async Task ExecuteHostActionAsync(
            SearchResultItem selected,
            SearchResultAction action)
        {
            var workspaceTarget = action.Kind switch
            {
                SearchActionKind.OpenWorkspaceModule => action.Target,
                SearchActionKind.OpenWorkflowReview => $"workflow:{action.Target}",
                _ => null,
            };
            if (workspaceTarget is not null)
            {
                ServicesInitializer.LauncherContinuity.Begin(
                    workspaceTarget,
                    new LauncherReturnIntent(
                        _originWindowHandle,
                        SearchBox.Text,
                        _contextSnapshot,
                        LauncherReturnMode.RestoreLauncher,
                        DateTimeOffset.UtcNow));
                if (!App.KeepPaletteVisibleForQuality)
                    Hide();
                await Task.Delay(40);
            }

            var result = await _actionExecutor.ExecuteAsync(action, _contextSnapshot);
            if (workspaceTarget is not null && !result.IsSuccess)
                ServicesInitializer.LauncherContinuity.Cancel(workspaceTarget);
            if (result.IsSuccess)
                await ServicesInitializer.SearchPreferences.RecordUseAsync(selected.Id);

            StatusText.Text = result.Message ?? (result.IsSuccess
                ? I18n("search.result.completed")
                : I18n("search.error.operationFailed"));
            StatusText.Foreground = (Brush)FindResource(result.IsSuccess
                ? "Long.Brush.State.Success"
                : "Long.Brush.State.Danger");
            Log.Debug(
                "Command Palette host action completed: Success={Success}, KeepOpen={KeepOpen}, Visible={Visible}, QualityKeepVisible={QualityKeepVisible}",
                result.IsSuccess,
                result.KeepPaletteOpen,
                IsVisible,
                App.KeepPaletteVisibleForQuality);
            if (result.IsSuccess && !result.KeepPaletteOpen)
            {
                Hide();
                return;
            }

            Show();
            Activate();
            SearchBox.Focus();
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter
                && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                && ResultsList.SelectedItem is
                    LauncherResultViewItem
                    {
                        Result: var selectedWithSecondary,
                    }
                && selectedWithSecondary.SecondaryActions.Count > 0)
            {
                e.Handled = true;
                await ExecuteHostActionAsync(
                    selectedWithSecondary,
                    selectedWithSecondary.SecondaryActions[0]);
                return;
            }

            if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                && ResultsList.SelectedItem is
                    LauncherResultViewItem
                    {
                        Result: { CanPin: true } selected,
                    })
            {
                var pinned = await ServicesInitializer.SearchPreferences.TogglePinnedAsync(selected.Id);
                StatusText.Text = pinned
                    ? I18n("palette.status.pinned")
                    : I18n("palette.status.unpinned");
                BeginSearch();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape) return;
            DismissAndRestoreOrigin();
            e.Handled = true;
        }

        private bool ExecuteQualityWindowAction(QualityWindowAction action)
        {
            switch (action)
            {
                case QualityWindowAction.ExecutePrimary:
                    _ = ExecuteSelectedAsync();
                    return ResultsList.SelectedItem is LauncherResultViewItem;
                case QualityWindowAction.ExecuteSecondary
                    when ResultsList.SelectedItem is
                        LauncherResultViewItem
                        {
                            Result: var selected,
                        }
                        && selected.SecondaryActions.Count > 0:
                    _ = ExecuteHostActionAsync(
                        selected,
                        selected.SecondaryActions[0]);
                    return true;
                case QualityWindowAction.Dismiss:
                    DismissAndRestoreOrigin();
                    return true;
                case QualityWindowAction.SelectDeterministicResult:
                    var query = SearchBox.Text.Trim();
                    var match = ResultsList.Items
                        .OfType<LauncherResultViewItem>()
                        .FirstOrDefault(item => string.Equals(
                            item.Title,
                            query,
                            StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        return false;
                    ResultsList.SelectedItem = match;
                    ResultsList.ScrollIntoView(match);
                    return true;
                default:
                    return false;
            }
        }

        private void DismissAndRestoreOrigin()
        {
            Hide();
            if (_originWindowHandle != nint.Zero)
                ForegroundWindowActivator.TryActivate(_originWindowHandle);
            _originWindowHandle = nint.Zero;
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            Log.Debug(
                "Command Palette deactivated: Visible={Visible}, QualityKeepVisible={QualityKeepVisible}",
                IsVisible,
                App.KeepPaletteVisibleForQuality);
            if (!App.KeepPaletteVisibleForQuality)
                Hide();
        }

        private static string I18n(string key)
            => ServicesInitializer.I18n.T(key);
    }
}
