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

    [Fact]
    public void QualityTransition_DoesNotPublishPhysicalPowerEvidence()
    {
        using var service = new HostEnvironmentStateService();
        var transitions = new List<HostPowerTransition>();
        service.PowerTransitionObserved += transitions.Add;

        service.SetInteractionAvailableForQuality(false);
        service.SetInteractionAvailableForQuality(true);

        Assert.Empty(transitions);
    }

    [Fact]
    public void QualityDisplayRefresh_DoesNotPublishPhysicalTopologyEvidence()
    {
        using var service = new HostEnvironmentStateService();
        var transitionCount = 0;
        service.DisplayTopologyChanged += () => transitionCount++;

        service.RefreshDisplayForQuality();

        Assert.Equal(0, transitionCount);
    }

    [Theory]
    [InlineData(0x0004, "Suspended")]
    [InlineData(0x0007, "ResumedFromSuspend")]
    [InlineData(0x0012, "ResumedAutomatically")]
    public void PowerBroadcast_MapsOnlyPhysicalSuspendAndResumeMessages(
        int notification,
        string expected)
        => Assert.Equal(
            expected,
            HostEnvironmentStateService.ParsePowerTransitionForQuality(
                notification)?.ToString());

    [Fact]
    public void PowerBroadcast_IgnoresUnrelatedNotifications()
        => Assert.Null(
            HostEnvironmentStateService.ParsePowerTransitionForQuality(0x000A));
}
