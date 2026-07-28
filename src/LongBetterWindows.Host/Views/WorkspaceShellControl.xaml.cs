using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkspaceShellControl : UserControl
    {
        private WorkspaceSessionCoordinator? _coordinator;
        private Func<WorkspaceModuleDescriptor, string>? _titleSelector;
        private Func<WorkspaceModuleKey, string>? _searchPlaceholderSelector;
        private Func<WorkspaceSearchRequest, Task>? _searchHandler;
        private readonly WorkspaceSearchSession _searchSession = new();
        private readonly DispatcherTimer _searchDebounce;
        private bool _updatingSearchText;

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
        internal event Action<Exception>? ScopedSearchFailed;

        internal void Bind(
            WorkspaceSessionCoordinator coordinator,
            Func<WorkspaceModuleDescriptor, string>? titleSelector = null,
            Func<WorkspaceModuleKey, string>? searchPlaceholderSelector = null,
            Func<WorkspaceSearchRequest, Task>? searchHandler = null)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            if (ReferenceEquals(_coordinator, coordinator)
                && ReferenceEquals(_titleSelector, titleSelector))
            {
                return;
            }

            Unsubscribe();
            _coordinator = coordinator;
            _titleSelector = titleSelector;
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
            var projected = WorkspaceShellProjection.Build(state, _titleSelector);
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
                state.ActiveModuleKey.Kind == "plugin-settings"
                    ? state.ActiveModuleKey.ResourceId
                    : null);
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
