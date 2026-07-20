using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private readonly CommandExecutor _executor;
        private readonly SearchResultActionExecutor _actionExecutor;
        private ContextSnapshot _contextSnapshot = ContextSnapshot.Empty;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _searchCts;
        private IntPtr _foregroundWindow;
        private IReadOnlyList<SearchResultItem> _allResults = Array.Empty<SearchResultItem>();
        private string _activeGroupId = SuperPanelGroupIds.Smart;
        private bool _lastSearchCompleted;
        private Point _dragStart;
        private SearchResultItem? _dragCandidate;
        private string? _dragSourceGroupId;
        private bool _suppressClick;
        private string? _editingGroupId;
        private HwndSource? _windowSource;
        private const int WmMouseWheel = 0x020A;

        private static readonly IReadOnlyList<PanelGroupDefinition> BuiltInGroupDefinitions = new[]
        {
            new PanelGroupDefinition(SuperPanelGroupIds.Smart, "智能推荐", "按上下文、相关性与使用习惯排序"),
            new PanelGroupDefinition(SuperPanelGroupIds.Pinned, "已固定", "可拖拽调整固定操作顺序"),
            new PanelGroupDefinition(SuperPanelGroupIds.Recent, "最近使用", "按最后成功执行时间排序"),
        };

        private SuperPanelWindow()
        {
            InitializeComponent();
            _plugins = HostProvider.Instance.PluginStore;
            _executor = new CommandExecutor(_plugins);
            _actionExecutor = new SearchResultActionExecutor(_plugins);
            RenderGroups();
            _plugins.PluginsChanged += OnPluginsChanged;
            Closed += (_, _) =>
            {
                _plugins.PluginsChanged -= OnPluginsChanged;
                if (_windowSource is not null)
                {
                    _windowSource.RemoveHook(WindowMessageHook);
                    _windowSource = null;
                }
                CancelOperations();
                _instance = null;
            };
        }

        public static void ShowPanel()
            => ShowPanelCore(null);

        internal static void ShowPanelForQuality()
            => ShowPanelCore(new ContextSnapshot(DateTimeOffset.UtcNow, new[]
            {
                new ContextItem
                {
                    Id = "quality.url",
                    Source = ContextSource.Clipboard,
                    Label = "剪贴板链接 · https://long.example/quality",
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
            _instance._foregroundWindow = foreground;
            if (presetContext is null)
                _instance.BeginLoad(request);
            else
                _instance.ApplyContext(presetContext);

            if (!_instance.IsVisible)
                _instance.Show();

            _instance.PositionNearCursor();
            _instance.Activate();
            _instance.AnimateIn();
            Log.Debug("Super Panel visible: {ElapsedMs:F1}ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        private void BeginLoad(ContextCaptureRequest request)
        {
            CancelOperations();
            _loadCts = new CancellationTokenSource();
            _contextSnapshot = ContextSnapshot.Empty;
            _allResults = Array.Empty<SearchResultItem>();
            _lastSearchCompleted = false;
            ContextBadges.ItemsSource = null;
            ContextBadges.Visibility = Visibility.Collapsed;
            ContextSummary.Text = "正在读取当前上下文…";
            RenderActiveGroup();
            BeginSearch();
            _ = LoadContextAsync(request, _loadCts.Token);
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
                ApplyContext(snapshot);
            }
            catch (OperationCanceledException)
            {
                // A newer invocation superseded this capture.
            }
        }

        private void ApplyContext(ContextSnapshot snapshot)
        {
            CancelOperations();
            _contextSnapshot = snapshot;
            _allResults = Array.Empty<SearchResultItem>();
            _lastSearchCompleted = false;
            ContextBadges.ItemsSource = snapshot.Items;
            ContextBadges.Visibility = snapshot.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            ContextSummary.Text = snapshot.Items.Count > 0
                ? $"已识别 {snapshot.Items.Count} 项上下文，操作将自动匹配"
                : "常用、固定与最近操作";
            RenderActiveGroup();
            BeginSearch();
        }

        private void BeginSearch()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            _ = RunSearchAsync(_searchCts.Token);
        }

        private async Task RunSearchAsync(CancellationToken cancellationToken)
        {
            try
            {
                StatusText.Text = "正在匹配…";
                var groupedResultIds = ServicesInitializer.SuperPanelGroups.GetGroups()
                    .SelectMany(group => group.ResultIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var results = await ServicesInitializer.Search.SearchIncrementalAsync(
                    new SearchRequest(
                        string.Empty,
                        _contextSnapshot,
                        24,
                        AdditionalPreferredResultIds: groupedResultIds),
                    snapshot =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                            ApplyResults(snapshot, completed: false);
                        return Task.CompletedTask;
                    },
                    cancellationToken,
                    metrics => Log.Debug(
                        "Super Panel search completed: FirstBatchMs={FirstBatchMs:F1}, TotalMs={TotalMs:F1}, Providers={ProviderCount}, Batches={BatchCount}, Results={ResultCount}",
                        metrics.FirstBatchElapsed?.TotalMilliseconds,
                        metrics.TotalElapsed.TotalMilliseconds,
                        metrics.ProviderCount,
                        metrics.BatchCount,
                        metrics.ResultCount));
                cancellationToken.ThrowIfCancellationRequested();
                ApplyResults(results, completed: true);
            }
            catch (OperationCanceledException)
            {
                // A newer context snapshot superseded this search.
            }
        }

        private void ApplyResults(IReadOnlyList<SearchResultItem> results, bool completed)
        {
            _allResults = results;
            _lastSearchCompleted = completed;
            RenderActiveGroup();
        }

        private void RenderActiveGroup()
        {
            var pinnedIds = ServicesInitializer.SearchPreferences.GetPinnedResultIds();
            var recentIds = ServicesInitializer.SearchPreferences.GetRecentResultIds(24);
            var customGroup = ServicesInitializer.SuperPanelGroups.GetGroups()
                .FirstOrDefault(group => string.Equals(
                    group.Id, _activeGroupId, StringComparison.OrdinalIgnoreCase));
            var visible = SuperPanelResultOrganizer.SelectGroup(
                _allResults,
                _activeGroupId,
                pinnedIds,
                recentIds,
                customGroup?.ResultIds,
                maxResults: 6);
            ResultsList.ItemsSource = visible;
            ResultsList.SelectedIndex = visible.Count > 0 ? 0 : -1;
            EmptyState.Visibility = _lastSearchCompleted && visible.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyStateText.Text = _activeGroupId switch
            {
                SuperPanelGroupIds.Pinned => "还没有固定操作",
                SuperPanelGroupIds.Recent => "还没有最近使用记录",
                _ when customGroup is not null => "把固定操作拖到这个分组",
                _ => "当前上下文没有可用操作",
            };
            StatusText.Text = visible.Count > 0
                ? $"{visible.Count} 个操作"
                : _lastSearchCompleted ? "当前分组为空" : "正在匹配…";
            InteractionHint.Text = _activeGroupId switch
            {
                SuperPanelGroupIds.Pinned => "拖拽排序或拖到文件夹 · 单击执行 · 滚轮切组",
                _ when customGroup is not null => "拖拽排序 · Delete 移出分组 · 滚轮切组",
                _ => "单击执行 · 滚轮切组 · Esc 返回原窗口",
            };
            CustomGroupActions.Visibility = customGroup is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
            RenderGroups(pinnedIds, recentIds);
        }

        private void RenderGroups(
            IReadOnlyList<string>? pinnedIds = null,
            IReadOnlyList<string>? recentIds = null)
        {
            pinnedIds ??= ServicesInitializer.SearchPreferences.GetPinnedResultIds();
            recentIds ??= ServicesInitializer.SearchPreferences.GetRecentResultIds(24);
            var customGroups = ServicesInitializer.SuperPanelGroups.GetGroups();
            var resultIds = _allResults.Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groups = BuiltInGroupDefinitions
                .Concat(customGroups.Select(group => new PanelGroupDefinition(
                    group.Id, group.Title, "拖入固定操作，建立自己的操作文件夹")))
                .ToList();
            GroupTabs.ItemsSource = groups.Select(group => new PanelGroupView(
                group.Id,
                group.Title,
                group.Hint,
                group.Id == _activeGroupId,
                group.Id switch
                {
                    SuperPanelGroupIds.Pinned => pinnedIds.Count(resultIds.Contains),
                    SuperPanelGroupIds.Recent => recentIds.Count(resultIds.Contains),
                    _ when SuperPanelGroupService.IsCustomGroupId(group.Id) => customGroups
                        .First(custom => string.Equals(
                            custom.Id, group.Id, StringComparison.OrdinalIgnoreCase))
                        .ResultIds.Count(resultIds.Contains),
                    _ => Math.Min(6, _allResults.Count),
                },
                SuperPanelGroupService.IsCustomGroupId(group.Id))).ToList();
        }

        private async Task ExecuteAsync(SearchResultItem selected)
        {
            if (selected.PrimaryAction.Kind == SearchActionKind.ContinueSearch)
            {
                Hide();
                CommandPaletteWindow.ShowPalette(selected.PrimaryAction.Target);
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
                StatusText.Text = "操作已失效";
                return;
            }

            var invocation = selected.PrimaryAction.Invocation
                ?? CommandInvocationFactory.Create(descriptor, _contextSnapshot);
            Hide();
            await Task.Delay(35);
            var result = await _executor.ExecuteAsync(descriptor.Key, invocation);
            if (result.IsSuccess)
                await ServicesInitializer.SearchPreferences.RecordUseAsync(selected.Id);

            if (result.IsSuccess && !result.KeepPaletteOpen)
                return;

            Show();
            PositionNearCursor();
            Activate();
            StatusText.Text = result.Message ?? "执行失败";
        }

        private void SecondaryActions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SearchResultItem selected } button
                || !selected.HasSecondaryActions) return;

            e.Handled = true;
            var menu = new ContextMenu { PlacementTarget = button };
            for (var index = 0; index < selected.SecondaryActions.Count; index++)
            {
                var action = selected.SecondaryActions[index];
                var item = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(action.Label) ? "执行" : action.Label,
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
            var result = await _actionExecutor.ExecuteAsync(action, _contextSnapshot);
            if (result.IsSuccess)
                await ServicesInitializer.SearchPreferences.RecordUseAsync(selected.Id);

            StatusText.Text = result.Message ?? (result.IsSuccess ? "操作已完成" : "操作失败");
            if (result.IsSuccess && !result.KeepPaletteOpen)
            {
                Hide();
                return;
            }

            Show();
            PositionNearCursor();
            Activate();
        }

        private void PositionNearCursor()
        {
            var placement = MonitorHelper.GetCursorPlacement(this);
            const double gap = 16;
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;
            var left = placement.Cursor.X + gap;
            var top = placement.Cursor.Y + gap;
            if (left + width > placement.WorkArea.Right - 10)
                left = placement.Cursor.X - width - gap;
            if (top + height > placement.WorkArea.Bottom - 10)
                top = placement.Cursor.Y - height - gap;
            Left = Math.Clamp(left, placement.WorkArea.Left + 10,
                Math.Max(placement.WorkArea.Left + 10, placement.WorkArea.Right - width - 10));
            Top = Math.Clamp(top, placement.WorkArea.Top + 10,
                Math.Max(placement.WorkArea.Top + 10, placement.WorkArea.Bottom - height - 10));
        }

        private void AnimateIn()
        {
            var duration = Application.Current.Resources["Long.Motion.Normal"] is Duration token
                ? token.TimeSpan
                : TimeSpan.FromMilliseconds(180);
            Opacity = duration == TimeSpan.Zero ? 1 : 0;
            var translate = new TranslateTransform(0, duration == TimeSpan.Zero ? 0 : 8);
            PanelChrome.RenderTransform = translate;
            if (duration == TimeSpan.Zero) return;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private void Dismiss(bool restoreFocus)
        {
            Hide();
            if (restoreFocus && _foregroundWindow != IntPtr.Zero)
                Shell32.SetForegroundWindow(_foregroundWindow);
        }

        private void CancelOperations()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
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

        private async void ResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragCandidate = null;
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;
            if (ResultsList.SelectedItem is SearchResultItem selected)
                await ExecuteAsync(selected);
        }

        private async void PinResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string resultId }) return;
            e.Handled = true;
            await ServicesInitializer.SearchPreferences.TogglePinnedAsync(resultId);
            BeginSearch();
        }

        private void GroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string groupId })
                SwitchGroup(groupId);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowMessageHook);
        }

        private IntPtr WindowMessageHook(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmMouseWheel)
                return IntPtr.Zero;

            var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
            CycleGroup(delta);
            handled = true;
            return IntPtr.Zero;
        }

        private void CycleGroup(int wheelDelta)
        {
            var groups = GetGroupDefinitions();
            var currentIndex = groups.FindIndex(group =>
                string.Equals(group.Id, _activeGroupId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) currentIndex = 0;
            var offset = wheelDelta < 0 ? 1 : -1;
            var nextIndex = (currentIndex + offset + groups.Count) % groups.Count;
            SwitchGroup(groups[nextIndex].Id);
        }

        private void SwitchGroup(string groupId)
        {
            if (!GetGroupDefinitions().Any(group => string.Equals(
                    group.Id, groupId, StringComparison.OrdinalIgnoreCase)))
                return;
            _activeGroupId = groupId;
            _dragCandidate = null;
            _dragSourceGroupId = null;
            RenderActiveGroup();
        }

        private void ResultsList_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            _dragCandidate = null;
            _dragSourceGroupId = null;
            _suppressClick = false;
            if ((_activeGroupId != SuperPanelGroupIds.Pinned
                 && !SuperPanelGroupService.IsCustomGroupId(_activeGroupId))
                || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
                return;

            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is not SearchResultItem result
                || (_activeGroupId == SuperPanelGroupIds.Pinned && !result.IsPinned))
                return;
            _dragStart = e.GetPosition(ResultsList);
            _dragCandidate = result;
            _dragSourceGroupId = _activeGroupId;
        }

        private void ResultsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
                return;
            var position = e.GetPosition(ResultsList);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var resultId = _dragCandidate.Id;
            _dragCandidate = null;
            _suppressClick = true;
            DragDrop.DoDragDrop(ResultsList, resultId, DragDropEffects.Move);
        }

        private void ResultsList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = (_activeGroupId == SuperPanelGroupIds.Pinned
                         || SuperPanelGroupService.IsCustomGroupId(_activeGroupId))
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
            var moved = _activeGroupId == SuperPanelGroupIds.Pinned
                ? await ServicesInitializer.SearchPreferences.MovePinnedAsync(resultId, targetIndex)
                : SuperPanelGroupService.IsCustomGroupId(_activeGroupId)
                    && await ServicesInitializer.SuperPanelGroups.MoveResultAsync(
                        _activeGroupId, resultId, targetIndex);
            if (targetIndex >= 0 && moved)
            {
                _dragSourceGroupId = null;
                RenderActiveGroup();
                StatusText.Text = _activeGroupId == SuperPanelGroupIds.Pinned
                    ? "固定顺序已保存"
                    : "分组顺序已保存";
            }
        }

        private void GroupButton_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = sender is Button { Tag: string groupId }
                        && SuperPanelGroupService.IsCustomGroupId(groupId)
                        && e.Data.GetDataPresent(typeof(string))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private async void GroupButton_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { Tag: string groupId }
                || !SuperPanelGroupService.IsCustomGroupId(groupId)
                || e.Data.GetData(typeof(string)) is not string resultId)
                return;
            if (await ServicesInitializer.SuperPanelGroups.AddResultAsync(groupId, resultId))
            {
                var sourceGroupId = _dragSourceGroupId;
                if (SuperPanelGroupService.IsCustomGroupId(sourceGroupId)
                    && !string.Equals(sourceGroupId, groupId, StringComparison.OrdinalIgnoreCase))
                    await ServicesInitializer.SuperPanelGroups.RemoveResultAsync(
                        sourceGroupId!, resultId);
                _dragSourceGroupId = null;
                SwitchGroup(groupId);
                StatusText.Text = "已移动到分组";
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
            => OpenGroupEditor(null, string.Empty);

        private void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = ServicesInitializer.SuperPanelGroups.GetGroups()
                .FirstOrDefault(item => string.Equals(
                    item.Id, _activeGroupId, StringComparison.OrdinalIgnoreCase));
            if (group is not null) OpenGroupEditor(group.Id, group.Title);
        }

        private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = ServicesInitializer.SuperPanelGroups.GetGroups()
                .FirstOrDefault(item => string.Equals(
                    item.Id, _activeGroupId, StringComparison.OrdinalIgnoreCase));
            if (group is null) return;
            var answer = MessageBox.Show(
                $"删除分组“{group.Title}”？分组中的操作不会被取消固定。",
                "删除操作分组", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            if (await ServicesInitializer.SuperPanelGroups.DeleteAsync(group.Id))
            {
                _activeGroupId = SuperPanelGroupIds.Pinned;
                RenderActiveGroup();
                StatusText.Text = "分组已删除";
            }
        }

        private void OpenGroupEditor(string? groupId, string title)
        {
            _editingGroupId = groupId;
            GroupEditorTitle.Text = groupId is null ? "新建操作分组" : "重命名操作分组";
            GroupNameTextBox.Text = title;
            GroupEditorPopup.IsOpen = true;
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
            var title = GroupNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(title))
            {
                StatusText.Text = "请输入分组名称";
                return;
            }
            if (_editingGroupId is null)
            {
                var group = await ServicesInitializer.SuperPanelGroups.CreateAsync(title);
                if (group is null)
                {
                    StatusText.Text = "最多创建 8 个分组";
                    return;
                }
                _activeGroupId = group.Id;
            }
            else if (!await ServicesInitializer.SuperPanelGroups.RenameAsync(
                         _editingGroupId, title))
            {
                StatusText.Text = "分组重命名失败";
                return;
            }
            GroupEditorPopup.IsOpen = false;
            _editingGroupId = null;
            RenderActiveGroup();
            StatusText.Text = "分组已保存";
        }

        private void CancelGroupEditor_Click(object sender, RoutedEventArgs e)
        {
            GroupEditorPopup.IsOpen = false;
            _editingGroupId = null;
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
                GroupEditorPopup.IsOpen = false;
                _editingGroupId = null;
                e.Handled = true;
            }
        }

        private void OpenCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            CommandPaletteWindow.ShowPalette();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Dismiss(restoreFocus: true);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter
                && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                && ResultsList.SelectedItem is SearchResultItem selectedWithSecondary
                && selectedWithSecondary.SecondaryActions.Count > 0)
            {
                _ = ExecuteHostActionAsync(
                    selectedWithSecondary,
                    selectedWithSecondary.SecondaryActions[0]);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete
                && SuperPanelGroupService.IsCustomGroupId(_activeGroupId)
                && ResultsList.SelectedItem is SearchResultItem grouped)
            {
                _ = RemoveFromActiveGroupAsync(grouped.Id);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && ResultsList.SelectedItem is SearchResultItem selected)
            {
                _ = ExecuteAsync(selected);
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Escape) return;
            Dismiss(restoreFocus: true);
            e.Handled = true;
        }

        private async Task RemoveFromActiveGroupAsync(string resultId)
        {
            if (await ServicesInitializer.SuperPanelGroups.RemoveResultAsync(
                    _activeGroupId, resultId))
            {
                RenderActiveGroup();
                StatusText.Text = "已移出分组";
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (!App.KeepSuperPanelVisibleForQuality)
                Hide();
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

        private sealed record PanelGroupDefinition(string Id, string Title, string Hint);

        private static List<PanelGroupDefinition> GetGroupDefinitions()
            => BuiltInGroupDefinitions.Concat(
                ServicesInitializer.SuperPanelGroups.GetGroups().Select(group =>
                    new PanelGroupDefinition(group.Id, group.Title, "自定义操作文件夹")))
                .ToList();

        private sealed record PanelGroupView(
            string Id,
            string Title,
            string Hint,
            bool IsActive,
            int Count,
            bool IsCustom);
    }
}
