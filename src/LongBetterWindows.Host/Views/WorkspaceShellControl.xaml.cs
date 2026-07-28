using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Host.Views
{
    public partial class WorkspaceShellControl : UserControl
    {
        private WorkspaceSessionCoordinator? _coordinator;
        private Func<WorkspaceModuleDescriptor, string>? _titleSelector;

        public WorkspaceShellControl()
        {
            InitializeComponent();
            ModuleItems.ItemsSource = ModuleTabs;
            Loaded += (_, _) => Subscribe();
            Unloaded += (_, _) => Unsubscribe();
        }

        internal ObservableCollection<WorkspaceModuleTabState> ModuleTabs { get; } = [];
        internal ToolCenterControl ToolCenter => ToolCenterContent;

        internal event Action<WorkspaceModuleKey>? ModuleActivationRequested;
        internal event Action<WorkspaceModuleKey>? ModuleCloseRequested;

        internal void Bind(
            WorkspaceSessionCoordinator coordinator,
            Func<WorkspaceModuleDescriptor, string>? titleSelector = null)
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
            Subscribe();
            ApplyState(coordinator.State);
        }

        internal void Refresh()
        {
            if (_coordinator is not null)
                ApplyState(_coordinator.State);
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
