using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.PluginSdk.Testing;
using SamplePlugin;

namespace LongBetterWindows.Tests;

public sealed class SamplePluginTests
{
    [Fact]
    public async Task HelloCommand_PreservesPluginSettingAcrossLanguageChanges()
    {
        var settings = new SettingsStub(new Dictionary<string, string>
        {
            ["audience"] = "Developer",
        });
        var notification = new NotificationStub();
        var host = new PluginTestHost()
            .Grant<IPluginSettingsService>(settings)
            .Grant<INotificationService>(notification);
        var plugin = new HelloPlugin();

        Assert.True(await plugin.InitializeAsync(host));
        Assert.True(await plugin.StartAsync());

        await plugin.OnLanguageChangedAsync(Language(
            "en-US",
            "Sample Plugin",
            "The sample plugin is ready for {0}",
            "Sample command completed successfully"));
        var english = await plugin.ExecuteCommandAsync(Command());

        await plugin.OnLanguageChangedAsync(Language(
            "zh-CN",
            "示例插件",
            "示例插件已为 {0} 准备就绪",
            "示例命令执行成功"));
        var chinese = await plugin.ExecuteCommandAsync(Command());

        Assert.True(english.IsSuccess);
        Assert.Equal(
            "The sample plugin is ready for Developer",
            english.Outputs["result"].Value);
        Assert.True(chinese.IsSuccess);
        Assert.Equal(
            "示例插件已为 Developer 准备就绪",
            chinese.Outputs["result"].Value);
        Assert.Equal(chinese.Outputs["result"].Value, notification.LastBody);
        Assert.Equal("示例插件", notification.LastTitle);
        Assert.Equal(2, notification.ShowCount);
    }

    private static PluginCommandInvocation Command() => new()
    {
        CommandId = "sample.hello",
    };

    private static PluginLanguageContext Language(
        string language,
        string pluginName,
        string ready,
        string success) => new(
            language,
            language,
            new Dictionary<string, string>
            {
                ["plugin.name"] = pluginName,
                ["toast.ready"] = ready,
                ["result.success"] = success,
            });

    private sealed class SettingsStub : IPluginSettingsService
    {
        private readonly Dictionary<string, string> _values;

        public SettingsStub(Dictionary<string, string> values)
        {
            _values = values;
        }

        public Task<HostApiResponse<string?>> GetAsync(string key) =>
            Task.FromResult(HostApiResponse<string?>.Success(
                _values.GetValueOrDefault(key)));

        public Task<HostApiResponse> SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.FromResult(HostApiResponse.Success());
        }
    }

    private sealed class NotificationStub : INotificationService
    {
        public string? LastTitle { get; private set; }
        public string? LastBody { get; private set; }
        public int ShowCount { get; private set; }

        public Task<HostApiResponse> ShowAsync(string title, string body)
        {
            LastTitle = title;
            LastBody = body;
            ShowCount++;
            return Task.FromResult(HostApiResponse.Success());
        }
    }
}
