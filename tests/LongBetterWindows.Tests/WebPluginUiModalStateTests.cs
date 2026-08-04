using System.Windows;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public sealed class WebPluginUiModalStateTests
{
    [Theory]
    [InlineData("{\"type\":\"long.ui-modal-state\",\"open\":true}", true)]
    [InlineData("{\"type\":\"long.ui-modal-state\",\"open\":false}", false)]
    public void TryRead_AcceptsTypedModalState(string json, bool expected)
    {
        Assert.True(WebPluginUiModalState.TryRead(json, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("{\"type\":\"long.ui-modal-state\"}")]
    [InlineData("{\"type\":\"long.ui-modal-state\",\"open\":\"true\"}")]
    [InlineData("{\"type\":\"ui.confirm\",\"open\":true}")]
    [InlineData("{\"type\":1,\"open\":true}")]
    [InlineData("not-json")]
    public void TryRead_RejectsMalformedOrUnrelatedMessages(string json)
        => Assert.False(WebPluginUiModalState.TryRead(json, out _));

    [Fact]
    public void AttachedState_DefaultsClosedAndCanReset()
    {
        var element = new DependencyObject();

        Assert.False(WebPluginUiModalState.IsOpen(element));
        WebPluginUiModalState.SetOpen(element, true);
        Assert.True(WebPluginUiModalState.IsOpen(element));
        WebPluginUiModalState.SetOpen(element, false);
        Assert.False(WebPluginUiModalState.IsOpen(element));
    }
}
