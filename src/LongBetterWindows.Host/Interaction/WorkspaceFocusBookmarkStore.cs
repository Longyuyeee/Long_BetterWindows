using System.Windows;
using System.Windows.Input;

namespace LongBetterWindows.Host.Interaction
{
    internal sealed class WorkspaceFocusBookmarkStore
    {
        private readonly Dictionary<WorkspaceModuleKey, WeakReference<IInputElement>>
            _bookmarks = new();

        public void Remember(WorkspaceModuleKey key, IInputElement element)
        {
            if (!key.IsValid)
                throw new ArgumentException(
                    "A valid workspace module key is required.",
                    nameof(key));
            ArgumentNullException.ThrowIfNull(element);
            _bookmarks[key] = new WeakReference<IInputElement>(element);
        }

        public bool Restore(WorkspaceModuleKey key)
        {
            if (!_bookmarks.TryGetValue(key, out var bookmark)
                || !bookmark.TryGetTarget(out var target)
                || target is not UIElement
                {
                    IsVisible: true,
                    IsEnabled: true,
                    Focusable: true,
                } element)
            {
                _bookmarks.Remove(key);
                return false;
            }

            return element.Focus();
        }

        public void Remove(WorkspaceModuleKey key)
            => _bookmarks.Remove(key);
    }
}
