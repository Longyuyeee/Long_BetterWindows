using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class HostEnvironmentStateServiceTests
{
    [Fact]
    public void QualityTransition_PublishesSuspendAndRestore()
    {
        using var service = new HostEnvironmentStateService();
        var transitions = new List<bool>();
        service.InteractionAvailabilityChanged += transitions.Add;

        service.SetInteractionAvailableForQuality(false);
        service.SetInteractionAvailableForQuality(true);

        Assert.True(service.IsInteractionAvailable);
        Assert.Equal([false, true], transitions);
    }
}
