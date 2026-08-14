using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace LongBetterWindows.Host.Controls;

/// <summary>
/// Visual-only text that must not duplicate the accessible name of its parent item.
/// </summary>
public sealed class DecorativeTextBlock : TextBlock
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
