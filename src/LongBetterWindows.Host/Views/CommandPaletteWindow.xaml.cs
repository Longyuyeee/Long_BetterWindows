using System.Windows;
using System.Windows.Automation;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private CancellationTokenSource? _contextCts;
        private CancellationTokenSource? _searchCts;

        private CommandPaletteWindow()
        {
            InitializeComponent();
            _plugins = HostProvider.Instance.PluginStore;
            _executor = new CommandExecutor(_plugins);
            _actionExecutor = new SearchResultActionExecutor(_plugins);
            _plugins.PluginsChanged += OnPluginsChanged;
            Closed += (_, _) =>
            {
                _plugins.PluginsChanged -= OnPluginsChanged;
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _contextCts?.Cancel();
                _contextCts?.Dispose();
                _instance = null;
            };
        }

        public static void ShowPalette()
            => ShowPalette(null);

        public static void ShowPalette(string? initialQuery)
        {
            var started = Stopwatch.GetTimestamp();
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ShowPalette(initialQuery));
                return;
            }

            var captureRequest = new ContextCaptureRequest(
                Shell32.GetForegroundWindow(),
                DateTimeOffset.UtcNow);
            _instance ??= new CommandPaletteWindow();
            _instance.SearchBox.Text = initialQuery ?? string.Empty;
            _instance.SearchBox.CaretIndex = _instance.SearchBox.Text.Length;
            _instance.BeginSearch();
            _instance.StatusText.Text = string.Empty;
            _instance.BeginLoadContext(captureRequest);

            if (!_instance.IsVisible)
                _instance.Show();

            _instance.Activate();
            _instance.SearchBox.Focus();
            _instance.AnimateIn();
            Log.Debug("Command Palette 可输入: {ElapsedMs:F1}ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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
            BeginSearch(debounce: true);
        }

        private void BeginSearch(bool debounce = false)
        {
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
                    await Task.Delay(45, token);

                StatusText.Foreground = (Brush)FindResource("Long.Brush.Text.Secondary");
                StatusText.Text = "正在搜索…";
                var request = new SearchRequest(
                    SearchBox.Text,
                    _contextSnapshot,
                    MaxResults: 12);
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

        private void RenderContextBadges()
        {
            ContextBadges.ItemsSource = _contextSnapshot.Items;
            ContextBadges.Visibility = _contextSnapshot.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
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

            var selectedId = (ResultsList.SelectedItem as SearchResultItem)?.Id;
            ResultsList.ItemsSource = results;
            var selectedIndex = selectedId is null
                ? -1
                : results.ToList().FindIndex(item =>
                    string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            ResultsList.SelectedIndex = selectedIndex >= 0
                ? selectedIndex
                : results.Count > 0 ? 0 : -1;
            EmptyState.Visibility = completed && results.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyStateText.Text = _plugins.Commands.Count == 0
                ? "插件仍在加载，请稍候…"
                : "没有匹配的命令";
            StatusText.Text = results.Count == 0
                ? completed ? "没有匹配的命令" : "正在搜索…"
                : $"{results.Count} 个结果";
        }

        private async Task ExecuteSelectedAsync()
        {
            if (ResultsList.SelectedItem is not SearchResultItem selected)
                return;

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
                StatusText.Text = "结果已失效，请重新搜索";
                return;
            }

            StatusText.Foreground = (Brush)FindResource("Long.Brush.Text.Secondary");
            StatusText.Text = "正在执行…";
            var invocation = selected.PrimaryAction.Invocation;
            if (invocation is null)
                invocation = CommandInvocationFactory.Create(descriptor, _contextSnapshot);

            // 先归还前台焦点，再执行依赖“当前窗口/Explorer”的命令。
            Hide();
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
            StatusText.Text = result.Message ?? "执行失败";
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
            var next = Math.Clamp(
                ResultsList.SelectedIndex + offset,
                0,
                ResultsList.Items.Count - 1);
            ResultsList.SelectedIndex = next;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }

        private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => await ExecuteSelectedAsync();

        private async void PinResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string resultId }) return;
            e.Handled = true;
            var pinned = await ServicesInitializer.SearchPreferences.TogglePinnedAsync(resultId);
            StatusText.Text = pinned ? "已固定到结果顶部" : "已取消固定";
            BeginSearch();
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
                && ResultsList.SelectedItem is SearchResultItem selectedWithSecondary
                && selectedWithSecondary.SecondaryActions.Count > 0)
            {
                e.Handled = true;
                await ExecuteHostActionAsync(
                    selectedWithSecondary,
                    selectedWithSecondary.SecondaryActions[0]);
                return;
            }

            if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                && ResultsList.SelectedItem is SearchResultItem { CanPin: true } selected)
            {
                var pinned = await ServicesInitializer.SearchPreferences.TogglePinnedAsync(selected.Id);
                StatusText.Text = pinned ? "已固定到结果顶部" : "已取消固定";
                BeginSearch();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
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
    }
}
