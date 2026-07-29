using System.Windows.Input;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceKeyboardRouterTests
{
    [Fact]
    public void SystemKey_IsNormalizedForAltNavigation()
    {
        Assert.Equal(
            Key.Left,
            MarketplaceKeyboardRouter.NormalizeKey(Key.System, Key.Left));
        Assert.Equal(
            Key.Enter,
            MarketplaceKeyboardRouter.NormalizeKey(Key.Enter, Key.None));
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void CompactList_OpensSelectedDetail(Key key)
    {
        var action = MarketplaceKeyboardRouter.Resolve(
            key,
            ModifierKeys.None,
            compactLayout: true,
            listFocused: true,
            hasSelection: true,
            confirmationVisible: false,
            confirmationBusy: false,
            detailOpen: false);

        Assert.Equal(MarketplaceKeyboardAction.OpenSelectedDetail, action);
    }

    [Fact]
    public void AltLeft_ReturnsFromDetail()
    {
        var action = MarketplaceKeyboardRouter.Resolve(
            Key.Left,
            ModifierKeys.Alt,
            compactLayout: true,
            listFocused: false,
            hasSelection: false,
            confirmationVisible: false,
            confirmationBusy: false,
            detailOpen: true);

        Assert.Equal(MarketplaceKeyboardAction.NavigateBack, action);
    }

    [Fact]
    public void Confirmation_EscapeDismissesOnlyWhenIdle()
    {
        var idle = MarketplaceKeyboardRouter.Resolve(
            Key.Escape,
            ModifierKeys.None,
            compactLayout: true,
            listFocused: false,
            hasSelection: false,
            confirmationVisible: true,
            confirmationBusy: false,
            detailOpen: true);
        var busy = MarketplaceKeyboardRouter.Resolve(
            Key.Escape,
            ModifierKeys.None,
            compactLayout: true,
            listFocused: false,
            hasSelection: false,
            confirmationVisible: true,
            confirmationBusy: true,
            detailOpen: true);

        Assert.Equal(MarketplaceKeyboardAction.DismissConfirmation, idle);
        Assert.Equal(MarketplaceKeyboardAction.None, busy);
    }

    [Fact]
    public void Confirmation_BlocksDetailNavigation()
    {
        var action = MarketplaceKeyboardRouter.Resolve(
            Key.Left,
            ModifierKeys.Alt,
            compactLayout: true,
            listFocused: false,
            hasSelection: false,
            confirmationVisible: true,
            confirmationBusy: false,
            detailOpen: true);

        Assert.Equal(MarketplaceKeyboardAction.None, action);
    }
}
