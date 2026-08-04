using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;
using Serilog;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkspaceShellControl : UserControl
    {
        private WorkspaceSessionCoordinator? _coordinator;
        private Func<WorkspaceModuleDescriptor, string>? _titleSelector;
        private Func<string, string>? _closeNameSelector;
        private Func<WorkspaceModuleKey, string>? _searchPlaceholderSelector;
        private Func<WorkspaceSearchRequest, Task>? _searchHandler;
        private readonly WorkspaceSearchSession _searchSession = new();
        private readonly DispatcherTimer _searchDebounce;
        private bool _updatingSearchText;
        private WorkspaceModuleKey? _runtimeModuleKey;
        private FrameworkElement? _runtimeContent;
        private Action? _runtimeShown;
        private Action? _runtimeHidden;
        private Action? _runtimeDetachRequested;
        private Func<Task>? _runtimeEndRequested;

        public WorkspaceShellControl()
        {
            InitializeComponent();
            ModuleItems.ItemsSource = ModuleTabs;
            _searchDebounce = new DispatcherTimer(
                TimeSpan.FromMilliseconds(180),
                DispatcherPriority.Background,
                SearchDebounce_Tick,
                Dispatcher)
            {
                IsEnabled = false,
            };
            Loaded += (_, _) => Subscribe();
            Unloaded += (_, _) =>
            {
                Unsubscribe();
                _searchDebounce.Stop();
            };
            SizeChanged += (_, _) =>
                InstalledPluginRail.SetCompact(ActualWidth < 980);
        }

        internal ObservableCollection<WorkspaceModuleTabState> ModuleTabs { get; } = [];
        internal ToolCenterControl ToolCenter => ToolCenterContent;
        internal bool HasOpenPluginUiModal
            => _runtimeContent is not null
                && ReferenceEquals(PluginRuntimeContent.Content, _runtimeContent)
                && WebPluginUiModalState.IsOpen(_runtimeContent);

        internal event Action<WorkspaceModuleKey>? ModuleActivationRequested;
        internal event Action<WorkspaceModuleKey>? ModuleCloseRequested;
        internal event Action<string>? PluginSettingsRequested
        {
            add => InstalledPluginRail.PluginSettingsRequested += value;
            remove => InstalledPluginRail.PluginSettingsRequested -= value;
        }
        internal event Action<string>? PluginRunRequested
        {
            add => InstalledPluginRail.PluginRunRequested += value;
            remove => InstalledPluginRail.PluginRunRequested -= value;
        }
        internal event Action<string>? PluginRuntimeSettingsRequested;
        internal event Action<Exception>? ScopedSearchFailed;

        internal bool IsHostingPluginRuntime(FrameworkElement content)
            => PluginRuntimeSurface.Visibility == Visibility.Visible
                && ReferenceEquals(PluginRuntimeContent.Content, content);

        internal void ShowPluginRuntime(
            WorkspaceModuleKey moduleKey,
            string title,
            FrameworkElement content,
            Action shown,
            Action hidden,
            Action detachRequested,
            Func<Task> endRequested)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (_runtimeContent is not null
                && !ReferenceEquals(_runtimeContent, content))
            {
                HidePluginRuntime(notifyHidden: true);
            }

            _runtimeModuleKey = moduleKey;
            _runtimeContent = content;
            _runtimeShown = shown;
            _runtimeHidden = hidden;
            _runtimeDetachRequested = detachRequested;
            _runtimeEndRequested = endRequested;
            PluginRuntimeTitle.Text = title;
            PluginRuntimeDetachedText.Visibility = Visibility.Collapsed;
            PluginRuntimeContent.Content = content;
            ToolCenterContent.Visibility = Visibility.Collapsed;
            PluginRuntimeSurface.Visibility = Visibility.Visible;
            PluginRuntimeEndButton.IsEnabled = true;
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                PluginRuntimeSurface,
                $"plugin-session:{moduleKey.InstanceId};placement:embedded");
            shown();
        }

        internal void ShowDetachedPluginRuntime(
            WorkspaceModuleKey moduleKey,
            string title,
            FrameworkElement content,
            Action shown,
            Action hidden,
            Action detachRequested,
            Func<Task> endRequested)
        {
            _runtimeModuleKey = moduleKey;
            _runtimeContent = content;
            _runtimeShown = shown;
            _runtimeHidden = hidden;
            _runtimeDetachRequested = detachRequested;
            _runtimeEndRequested = endRequested;
            PluginRuntimeTitle.Text = title;
            PluginRuntimeContent.Content = null;
            PluginRuntimeDetachedText.Visibility = Visibility.Visible;
            ToolCenterContent.Visibility = Visibility.Collapsed;
            PluginRuntimeSurface.Visibility = Visibility.Visible;
            PluginRuntimeEndButton.IsEnabled = true;
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                PluginRuntimeSurface,
                $"plugin-session:{moduleKey.InstanceId};placement:detached");
        }

        internal bool HidePluginRuntime(bool notifyHidden)
        {
            if (PluginRuntimeSurface.Visibility != Visibility.Visible)
                return false;
            var wasAttached = _runtimeContent is not null
                && ReferenceEquals(
                    PluginRuntimeContent.Content,
                    _runtimeContent);
            PluginRuntimeContent.Content = null;
            PluginRuntimeSurface.Visibility = Visibility.Collapsed;
            ToolCenterContent.Visibility = Visibility.Visible;
            if (notifyHidden && wasAttached)
                _runtimeHidden?.Invoke();
            return true;
        }

        internal void ReleasePluginRuntime(FrameworkElement content)
        {
            if (!ReferenceEquals(_runtimeContent, content))
                return;
            PluginRuntimeContent.Content = null;
            PluginRuntimeSurface.Visibility = Visibility.Collapsed;
            ToolCenterContent.Visibility = Visibility.Visible;
            ClearPluginRuntimeState();
        }

        internal void RemovePluginRuntime(WorkspaceModuleKey key)
        {
            if (_runtimeModuleKey != key)
                return;
            PluginRuntimeContent.Content = null;
            PluginRuntimeSurface.Visibility = Visibility.Collapsed;
            ToolCenterContent.Visibility = Visibility.Visible;
            ClearPluginRuntimeState();
        }

        internal bool DetachActivePluginRuntime()
        {
            if (_runtimeContent is null
                || !ReferenceEquals(
                    PluginRuntimeContent.Content,
                    _runtimeContent))
            {
                return false;
            }
            PluginRuntimeContent.Content = null;
            PluginRuntimeDetachedText.Visibility = Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                PluginRuntimeSurface,
                $"plugin-session:{_runtimeModuleKey?.InstanceId};placement:detached");
            _runtimeDetachRequested?.Invoke();
            return true;
        }

        internal async Task<bool> EndActivePluginRuntimeAsync()
        {
            if (_runtimeEndRequested is null)
                return false;
            await _runtimeEndRequested();
            return true;
        }

        internal (
            string? ModuleKey,
            string? SessionId,
            int ContentIdentity,
            bool IsVisible,
            bool IsDetached) GetPluginRuntimeQualityState()
        {
            var isVisible =
                PluginRuntimeSurface.Visibility == Visibility.Visible;
            var isAttached = _runtimeContent is not null
                && ReferenceEquals(
                    PluginRuntimeContent.Content,
                    _runtimeContent);
            return (
                _runtimeModuleKey?.ToString(),
                _runtimeModuleKey?.InstanceId,
                _runtimeContent is null
                    ? 0
                    : RuntimeHelpers.GetHashCode(_runtimeContent),
                isVisible,
                isVisible && !isAttached);
        }

        internal FrameworkElement? GetPluginRuntimeContentForQuality()
            => _runtimeContent;

        private void ClearPluginRuntimeState()
        {
            _runtimeModuleKey = null;
            _runtimeContent = null;
            _runtimeShown = null;
            _runtimeHidden = null;
            _runtimeDetachRequested = null;
            _runtimeEndRequested = null;
            PluginRuntimeDetachedText.Visibility = Visibility.Collapsed;
            PluginRuntimeEndButton.IsEnabled = true;
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                PluginRuntimeSurface,
                string.Empty);
        }

        internal void Bind(
            WorkspaceSessionCoordinator coordinator,
            Func<WorkspaceModuleDescriptor, string>? titleSelector = null,
            Func<string, string>? closeNameSelector = null,
            Func<WorkspaceModuleKey, string>? searchPlaceholderSelector = null,
            Func<WorkspaceSearchRequest, Task>? searchHandler = null)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            if (ReferenceEquals(_coordinator, coordinator)
                && ReferenceEquals(_titleSelector, titleSelector)
                && ReferenceEquals(_closeNameSelector, closeNameSelector))
            {
                return;
            }

            Unsubscribe();
            _coordinator = coordinator;
            _titleSelector = titleSelector;
            _closeNameSelector = closeNameSelector;
            _searchPlaceholderSelector = searchPlaceholderSelector;
            _searchHandler = searchHandler;
            Subscribe();
            ApplyState(coordinator.State);
        }

        internal void Refresh()
        {
            if (_coordinator is not null)
                ApplyState(_coordinator.State);
        }

        internal void ApplyLanguage()
        {
            InstalledPluginRail.ApplyLanguage();
            Refresh();
        }

        internal void FocusActiveModule()
        {
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    var active = ModuleTabs.FirstOrDefault(tab => tab.IsActive);
                    if (active is null)
                        return;
                    FindModuleButton(this, active.Key)?.Focus();
                },
                DispatcherPriority.Input);
        }

        internal bool HasScopedSearchQuery
            => _searchSession.ActiveModuleKey is { } key
                && !string.IsNullOrEmpty(_searchSession.GetQuery(key));

        internal bool FocusScopedSearch()
        {
            if (WorkspaceSearchHost.Visibility != Visibility.Visible)
                return false;
            WorkspaceSearchBox.Focus();
            Keyboard.Focus(WorkspaceSearchBox);
            WorkspaceSearchBox.SelectAll();
            return true;
        }

        internal bool ClearScopedSearch()
        {
            if (!HasScopedSearchQuery)
                return false;
            WorkspaceSearchBox.Clear();
            return true;
        }

        internal void RemoveModuleSearch(WorkspaceModuleKey key)
            => _searchSession.RemoveModule(key);

        private void Subscribe()
        {
            if (_coordinator is not null)
            {
                _coordinator.StateChanged -= Coordinator_StateChanged;
                _coordinator.StateChanged += Coordinator_StateChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_coordinator is not null)
                _coordinator.StateChanged -= Coordinator_StateChanged;
        }

        private void Coordinator_StateChanged(
            object? sender,
            WorkspaceNavigationChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(() => ApplyState(e.Current));
                return;
            }
            ApplyState(e.Current);
        }

        private void ApplyState(WorkspaceNavigationState state)
        {
            var projected = WorkspaceShellProjection.Build(
                state,
                _titleSelector,
                _closeNameSelector);
            for (var index = ModuleTabs.Count - 1; index >= 0; index--)
            {
                if (!projected.Any(tab => tab.Key == ModuleTabs[index].Key))
                    ModuleTabs.RemoveAt(index);
            }

            for (var targetIndex = 0; targetIndex < projected.Count; targetIndex++)
            {
                var target = projected[targetIndex];
                var currentIndex = IndexOf(target.Key);
                if (currentIndex < 0)
                {
                    ModuleTabs.Insert(targetIndex, target);
                    continue;
                }

                if (currentIndex != targetIndex)
                    ModuleTabs.Move(currentIndex, targetIndex);
                if (ModuleTabs[targetIndex] != target)
                    ModuleTabs[targetIndex] = target;
            }

            ActivateSearchScope(state.ActiveModuleKey);
            InstalledPluginRail.SetActivePlugin(
                state.ActiveModuleKey.Kind is "plugin-settings" or "plugin-runtime"
                    ? state.ActiveModuleKey.ResourceId
                    : null);
        }

        private void PluginRuntimeSettings_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_runtimeModuleKey is { } key)
                PluginRuntimeSettingsRequested?.Invoke(key.ResourceId);
        }

        private void PluginRuntimeDetach_Click(
            object sender,
            RoutedEventArgs e)
            => DetachActivePluginRuntime();

        private async void PluginRuntimeEnd_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_runtimeEndRequested is null)
                return;
            PluginRuntimeEndButton.IsEnabled = false;
            try
            {
                await EndActivePluginRuntimeAsync();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Could not end workspace plugin runtime");
                PluginRuntimeEndButton.IsEnabled = true;
            }
        }

        private void ActivateSearchScope(WorkspaceModuleKey key)
        {
            var scope = WorkspaceSearchScopeCatalog.Resolve(key);
            var query = _searchSession.ActivateModule(key);
            _searchDebounce.Stop();
            _updatingSearchText = true;
            WorkspaceSearchBox.Text = query;
            _updatingSearchText = false;
            WorkspaceSearchHost.Visibility = scope is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (scope is null)
                return;

            WorkspaceSearchHint.Text = _searchPlaceholderSelector?.Invoke(key)
                ?? scope.PlaceholderResourceKey;
            System.Windows.Automation.AutomationProperties.SetName(
                WorkspaceSearchBox,
                WorkspaceSearchHint.Text);
            ScheduleSearch();
        }

        private void WorkspaceSearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ClearWorkspaceSearchButton.Visibility =
                string.IsNullOrEmpty(WorkspaceSearchBox.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            if (_updatingSearchText
                || _searchSession.ActiveModuleKey is not { } key)
            {
                return;
            }

            _searchSession.SetQuery(key, WorkspaceSearchBox.Text);
            ScheduleSearch();
        }

        private void ClearWorkspaceSearch_Click(
            object sender,
            RoutedEventArgs e)
        {
            WorkspaceSearchBox.Clear();
            WorkspaceSearchBox.Focus();
        }

        private void ScheduleSearch()
        {
            if (WorkspaceSearchHost.Visibility != Visibility.Visible
                || _searchHandler is null)
            {
                return;
            }
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private async void SearchDebounce_Tick(object? sender, EventArgs e)
        {
            _searchDebounce.Stop();
            if (_searchHandler is null)
                return;
            var request = _searchSession.BeginSearch();
            try
            {
                await _searchHandler(request);
                _searchSession.Complete(request);
            }
            catch (OperationCanceledException)
                when (request.CancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ScopedSearchFailed?.Invoke(exception);
            }
        }

        private int IndexOf(WorkspaceModuleKey key)
        {
            for (var index = 0; index < ModuleTabs.Count; index++)
            {
                if (ModuleTabs[index].Key == key)
                    return index;
            }
            return -1;
        }

        private void ModuleTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: WorkspaceModuleKey key })
                return;
            var active = ModuleTabs.FirstOrDefault(tab => tab.Key == key);
            if (active?.IsActive == true)
                return;
            ModuleActivationRequested?.Invoke(key);
        }

        private void ModuleClose_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button { Tag: WorkspaceModuleKey key })
                ModuleCloseRequested?.Invoke(key);
        }

        private static Button? FindModuleButton(
            DependencyObject root,
            WorkspaceModuleKey key)
        {
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
                if (child is Button { Tag: WorkspaceModuleKey candidate } button
                    && candidate == key
                    && button.Name == "ModuleTabButton")
                {
                    return button;
                }

                var nested = FindModuleButton(child, key);
                if (nested is not null)
                    return nested;
            }
            return null;
        }
    }
}
