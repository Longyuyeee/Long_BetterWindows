using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class PanelExpansionIntentTests
{
    [Fact]
    public void Consume_PreservesPanelStateOnceAndClearsSensitivePayload()
    {
        var context = CreateContext();
        var intent = new PanelExpansionIntent(
            (nint)42,
            "image",
            context,
            "plugin.ocr",
            context.CapturedAt);

        var state = intent.Consume();

        Assert.Equal((nint)42, state.OriginWindowHandle);
        Assert.Equal("image", state.Query);
        Assert.Same(context, state.Context);
        Assert.Equal("plugin.ocr", state.SelectedResultId);
        Assert.True(intent.IsConsumed);
        Assert.Empty(intent.Context.Items);
        Assert.Throws<InvalidOperationException>(() => intent.Consume());
    }

    [Fact]
    public void SerializationAndDisplay_DoNotExposePanelPayload()
    {
        var context = CreateContext();
        var intent = new PanelExpansionIntent(
            (nint)42,
            "private query",
            context,
            "private.result",
            context.CapturedAt);

        var json = JsonSerializer.Serialize(intent);
        var display = intent.ToString();

        Assert.DoesNotContain("private query", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private.result", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private query", display, StringComparison.Ordinal);
        Assert.Contains("\"ContextItemCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"HasSelection\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Discard_IsIdempotentAndClearsContext()
    {
        var context = CreateContext();
        var intent = new PanelExpansionIntent(
            nint.Zero,
            null,
            context,
            null,
            context.CapturedAt);

        intent.Discard();
        intent.Discard();

        Assert.True(intent.IsConsumed);
        Assert.Empty(intent.Context.Items);
    }

    private static ContextSnapshot CreateContext()
    {
        var capturedAt = DateTimeOffset.Parse("2026-08-06T12:00:00+08:00");
        return new ContextSnapshot(
            capturedAt,
            [
                new ContextItem
                {
                    Id = "fixture",
                    Source = ContextSource.Clipboard,
                    Label = "Sensitive fixture",
                    Text = "secret text",
                    CompatibleInputTypes = [AcceptedInputType.Text],
                    Sensitivity = ContextSensitivity.Sensitive,
                },
            ]);
    }
}
