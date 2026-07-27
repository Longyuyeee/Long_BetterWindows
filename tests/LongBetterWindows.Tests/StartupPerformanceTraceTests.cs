using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class StartupPerformanceTraceTests
{
    [Fact]
    public async Task WriteAsync_ProducesOrderedResourceSamples()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LongBetterWindows.Tests",
            Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(directory, "startup.json");

        try
        {
            var trace = new StartupPerformanceTrace(reportPath);
            trace.Mark("first");
            trace.Mark("second");
            await trace.WriteAsync(
                new PluginRuntimeStartResult(25, 0, 0, null),
                managementCardShadows: true,
                managementCardShadowCount: 21);

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var root = report.RootElement;
            Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
            Assert.Equal(25, root.GetProperty("loaded_plugin_count").GetInt32());
            Assert.True(root.GetProperty("management_card_shadows").GetBoolean());
            Assert.Equal(
                21,
                root.GetProperty("management_card_shadow_count").GetInt32());
            var samples = root.GetProperty("samples");
            Assert.Equal(2, samples.GetArrayLength());
            Assert.Equal("first", samples[0].GetProperty("stage").GetString());
            Assert.Equal("second", samples[1].GetProperty("stage").GetString());
            Assert.True(samples[1].GetProperty("elapsed_ms").GetDouble()
                        >= samples[0].GetProperty("elapsed_ms").GetDouble());
            Assert.True(samples[1].GetProperty("private_memory_mb").GetDouble() > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
