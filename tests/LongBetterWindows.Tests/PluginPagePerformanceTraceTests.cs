using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class PluginPagePerformanceTraceTests
{
    [Fact]
    public async Task WriteAsync_ProducesResourceAndVisualSamples()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LongBetterWindows.Tests",
            Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(directory, "plugin-page.json");

        try
        {
            var trace = new PluginPagePerformanceTrace(
                reportPath,
                ["com.long.sample"]);
            trace.Mark(
                "projection",
                new PluginPageVisualMetrics(25, 8, 240));
            trace.SetWindowMessageCheckpoints(
            [
                new WindowMessageCheckpoint(
                    "projection",
                    3,
                    [new WindowMessageCount(0x000F, "WM_PAINT", 3)]),
            ]);
            trace.SetWindowVisibleDuringIdle(false);
            await trace.WriteAsync(
                new PluginRuntimeStartResult(25, 0, 0, null),
                commandCount: 42,
                idleMilliseconds: 9_000,
                runningPluginIds: ["com.long.running"]);

            using var report = JsonDocument.Parse(
                await File.ReadAllTextAsync(reportPath));
            var root = report.RootElement;
            Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
            Assert.Equal(9_000, root.GetProperty("idle_ms").GetInt32());
            Assert.Equal(25, root.GetProperty("loaded_plugin_count").GetInt32());
            Assert.Equal(42, root.GetProperty("command_count").GetInt32());
            Assert.True(root.GetProperty("ui_thread_id").GetInt32() > 0);
            Assert.Equal(
                "com.long.sample",
                root.GetProperty("suppressed_auto_start_plugin_ids")[0]
                    .GetString());
            Assert.Equal(
                "com.long.running",
                root.GetProperty("running_plugin_ids")[0].GetString());
            Assert.False(
                root.GetProperty("window_visible_during_idle").GetBoolean());
            var messageCheckpoint =
                root.GetProperty("window_message_checkpoints")[0];
            Assert.Equal(
                "WM_PAINT",
                messageCheckpoint.GetProperty("top_messages")[0]
                    .GetProperty("name")
                    .GetString());
            var sample = root.GetProperty("samples")[0];
            Assert.Equal("projection", sample.GetProperty("stage").GetString());
            Assert.True(sample.GetProperty("top_threads").GetArrayLength() > 0);
            Assert.Equal(25, sample.GetProperty("item_count").GetInt32());
            Assert.Equal(
                8,
                sample.GetProperty("realized_container_count").GetInt32());
            Assert.Equal(
                240,
                sample.GetProperty("visual_descendant_count").GetInt32());
            Assert.Equal(
                0,
                sample.GetProperty("animated_property_count").GetInt32());
            Assert.True(sample.GetProperty("private_memory_mb").GetDouble() > 0);
            Assert.True(sample.GetProperty("gc_committed_mb").GetDouble() > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
