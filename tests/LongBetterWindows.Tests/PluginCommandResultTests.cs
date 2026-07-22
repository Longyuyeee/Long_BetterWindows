using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Tests;

public sealed class PluginCommandResultTests
{
    [Fact]
    public void Success_DefaultsToEmptyStructuredOutputs()
    {
        var result = PluginCommandResult.Success("done");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Outputs);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public void Success_DefensivelyCopiesTypedOutputs()
    {
        var outputs = new Dictionary<string, PluginCommandOutput>
        {
            ["path"] = new(PluginCommandOutputType.Path, "C:\\original.txt"),
        };

        var result = PluginCommandResult.Success(outputs: outputs);
        outputs["path"] = new PluginCommandOutput(
            PluginCommandOutputType.Path,
            "C:\\changed.txt");

        Assert.Equal("C:\\original.txt", result.Outputs["path"].Value);
    }
}
