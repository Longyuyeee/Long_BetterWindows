namespace LongBetterWindows.Host.Engine
{
    public partial class WebPluginRuntime
    {
        internal void NotifyWidgetLayoutChanged(WidgetSurfaceLayout layout)
            => _widgetLifecycle?.Resize(layout);

        internal void NotifyWidgetVisibilityChanged(bool visible, string reason)
            => _widgetLifecycle?.SetVisibility(visible, reason);

        internal void SuspendWidget(string reason = "hidden", int deadlineMs = 30_000)
            => _widgetLifecycle?.Suspend(reason, deadlineMs);

        internal void ResumeWidget(string reason = "visible")
            => _widgetLifecycle?.Resume(reason);
    }
}
