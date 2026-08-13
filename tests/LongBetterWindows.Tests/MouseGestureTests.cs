using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class MouseGestureTests
{
    [Fact]
    public void LongRightPress_ShortClickPassesThrough()
    {
        var recognizer = new LongRightPressRecognizer();
        recognizer.Begin(100, 200, 1_000);

        Assert.False(recognizer.TryTrigger(102, 202, 1_300));
        Assert.False(recognizer.End());
    }

    [Fact]
    public void LongRightPress_HoldTriggersAndSuppressesOnlyRecognizedMenu()
    {
        var recognizer = new LongRightPressRecognizer();
        recognizer.Begin(100, 200, 1_000);

        Assert.True(recognizer.TryTrigger(
            104, 204, 1_000 + LongRightPressRecognizer.HoldMilliseconds));
        Assert.True(recognizer.End());
        Assert.False(recognizer.End());
    }

    [Fact]
    public void LongRightPress_MovementCancelsGesture()
    {
        var recognizer = new LongRightPressRecognizer();
        recognizer.Begin(100, 200, 1_000);

        Assert.False(recognizer.TryTrigger(
            100 + LongRightPressRecognizer.MovementTolerance + 1,
            200,
            2_000));
        Assert.False(recognizer.End());
    }

    [Fact]
    public async Task GestureMode_DefaultsDisabledAndPersistsExplicitChoice()
    {
        var storage = new MemoryStorage();
        using var service = new MouseGestureService(storage);
        await service.InitializeAsync();
        Assert.Equal(MouseGestureMode.Disabled, service.Mode);

        await service.SetModeAsync(MouseGestureMode.LongRightPress);
        using var reloaded = new MouseGestureService(storage);
        await reloaded.InitializeAsync();

        Assert.Equal(MouseGestureMode.LongRightPress, reloaded.Mode);
        var raw = Assert.Single(storage.Values).Value;
        Assert.Contains("LongRightPress", raw);
        Assert.DoesNotContain("Query", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Path", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GestureHookPolicy_FollowsHostInteractionAvailability()
    {
        Assert.True(MouseGestureService.ShouldInstallHook(
            started: true,
            MouseGestureMode.MiddleButton,
            interactionAvailable: true));
        Assert.False(MouseGestureService.ShouldInstallHook(
            started: true,
            MouseGestureMode.MiddleButton,
            interactionAvailable: false));
        Assert.False(MouseGestureService.ShouldInstallHook(
            started: true,
            MouseGestureMode.Disabled,
            interactionAvailable: true));
        Assert.False(MouseGestureService.ShouldInstallHook(
            started: false,
            MouseGestureMode.MiddleButton,
            interactionAvailable: true));
    }

    private sealed class MemoryStorage : IStorageService
    {
        public Dictionary<string, string> Values { get; } = new();

        public Task<HostApiResponse<string?>> GetAsync(string key)
            => Task.FromResult(HostApiResponse<string?>.Success(
                Values.TryGetValue(key, out var value) ? value : null));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse> DeleteAsync(string key)
        {
            Values.Remove(key);
            return Task.FromResult(HostApiResponse.Success());
        }

        public Task<HostApiResponse<bool>> ContainsKeyAsync(string key)
            => Task.FromResult(HostApiResponse<bool>.Success(Values.ContainsKey(key)));
    }
}
