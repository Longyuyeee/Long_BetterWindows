using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace LongBetterWindows.Host.Automation
{
    internal static class AccessibilityLiveRegion
    {
        internal static void Announce(TextBlock target, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || target.Text == message)
                return;

            target.Text = message;
            var peer = UIElementAutomationPeer.FromElement(target)
                ?? UIElementAutomationPeer.CreatePeerForElement(target);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
