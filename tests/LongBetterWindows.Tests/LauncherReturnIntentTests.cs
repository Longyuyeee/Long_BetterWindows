using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class LauncherReturnIntentTests
{
    [Fact]
    public void SerializationAndToString_DoNotExposeSensitivePayload()
    {
        var intent = CreateIntent(
            LauncherReturnMode.RestoreLauncher,
            query: "private query",
            text: "secret text",
            path: @"C:\private\secret.txt");

        var json = JsonSerializer.Serialize(intent);
        var display = intent.ToString();

        Assert.DoesNotContain("private query", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private query", display, StringComparison.Ordinal);
        Assert.DoesNotContain("secret text", display, StringComparison.Ordinal);
        Assert.Contains("\"ContextItemCount\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"HasQuery\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Consume_RestoresLauncherStateOnceAndClearsIntentPayload()
    {
        var intent = CreateIntent(
            LauncherReturnMode.RestoreLauncher,
            query: "market",
            text: "clipboard",
            path: @"C:\fixture.txt");

        var state = intent.Consume(originWindowIsAvailable: true);

        Assert.Equal(LauncherFocusTarget.LauncherInput, state.FocusTarget);
        Assert.Equal("market", state.Query);
        Assert.Equal("clipboard", Assert.Single(state.Context.Items).Text);
        Assert.Equal(nint.Zero, state.OriginWindowHandle);
        Assert.True(intent.IsConsumed);
        Assert.Equal(string.Empty, intent.Query);
        Assert.Empty(intent.Context.Items);
        Assert.Equal(nint.Zero, intent.OriginWindowHandle);
        Assert.Throws<InvalidOperationException>(() =>
            intent.Consume(originWindowIsAvailable: true));
    }

    [Fact]
    public void RestoreOriginWindow_UsesValidOriginAndFallsBackDeterministically()
    {
        var valid = CreateIntent(
            LauncherReturnMode.RestoreOriginWindow,
            origin: (nint)42);
        var invalid = CreateIntent(
            LauncherReturnMode.RestoreOriginWindow,
            origin: (nint)84);

        var restored = valid.Consume(originWindowIsAvailable: true);
        var fallback = invalid.Consume(originWindowIsAvailable: false);

        Assert.Equal(LauncherFocusTarget.OriginWindow, restored.FocusTarget);
        Assert.Equal((nint)42, restored.OriginWindowHandle);
        Assert.Equal(LauncherFocusTarget.LauncherInput, fallback.FocusTarget);
        Assert.Equal(nint.Zero, fallback.OriginWindowHandle);
    }

    [Fact]
    public void ClearLauncher_DoesNotRestoreQueryOrContext()
    {
        var intent = CreateIntent(
            LauncherReturnMode.ClearLauncher,
            query: "discard me",
            text: "discard me too");

        var state = intent.Consume(originWindowIsAvailable: true);

        Assert.Equal(LauncherFocusTarget.LauncherInput, state.FocusTarget);
        Assert.Equal(string.Empty, state.Query);
        Assert.Empty(state.Context.Items);
    }

    [Fact]
    public void RestoreLauncher_PreservesAvailableOriginForExplicitDismissal()
    {
        var intent = CreateIntent(
            LauncherReturnMode.RestoreLauncher,
            origin: (nint)42,
            query: "market");

        var state = intent.Consume(originWindowIsAvailable: true);

        Assert.Equal(LauncherFocusTarget.LauncherInput, state.FocusTarget);
        Assert.Equal((nint)42, state.OriginWindowHandle);
        Assert.Equal("market", state.Query);
    }

    [Fact]
    public void Discard_ClearsPayloadAndIsIdempotent()
    {
        var intent = CreateIntent(
            LauncherReturnMode.RestoreLauncher,
            query: "discard",
            text: "secret");

        intent.Discard();
        intent.Discard();

        Assert.True(intent.IsConsumed);
        Assert.Equal(string.Empty, intent.Query);
        Assert.Empty(intent.Context.Items);
    }

    private static LauncherReturnIntent CreateIntent(
        LauncherReturnMode mode,
        nint origin = default,
        string query = "",
        string text = "text",
        string path = @"C:\fixture.txt")
    {
        var capturedAt = DateTimeOffset.Parse("2026-07-28T10:00:00+08:00");
        var context = new ContextSnapshot(
            capturedAt,
            [
                new ContextItem
                {
                    Id = "fixture",
                    Source = ContextSource.Clipboard,
                    Label = "Sensitive fixture",
                    Text = text,
                    Paths = [path],
                    ImagePng = [1, 2, 3, 4],
                    CompatibleInputTypes = [AcceptedInputType.Text],
                    Sensitivity = ContextSensitivity.Sensitive,
                },
            ]);
        return new LauncherReturnIntent(origin, query, context, mode, capturedAt);
    }
}
