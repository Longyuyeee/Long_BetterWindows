using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public class CoreTests
{
    [Fact]
    public void ManifestErrorCodes_HaveStablePublishedValues()
    {
        Assert.Equal(0, (int)ManifestErrorCode.None);
        Assert.Equal(1000, (int)ManifestErrorCode.FileNotFound);
        Assert.Equal(1001, (int)ManifestErrorCode.ReadFailed);
        Assert.Equal(1002, (int)ManifestErrorCode.InvalidJson);
        Assert.Equal(1003, (int)ManifestErrorCode.ValidationFailed);
        Assert.Equal(2000, (int)ManifestValidationCode.InvalidManifestValue);
        Assert.Equal(2001, (int)ManifestValidationCode.InvalidCommand);
        Assert.Equal(2002, (int)ManifestValidationCode.InvalidWindow);
        Assert.Equal(2003, (int)ManifestValidationCode.IncompatibleApiVersion);
        Assert.Equal(2004, (int)ManifestValidationCode.InvalidLocalization);
        Assert.Equal(2005, (int)ManifestValidationCode.InvalidWidget);
    }

    [Fact]
    public void ApiVersion_IsCompatible_HostNewerThanPlugin_ReturnsTrue()
    {
        var host = new ApiVersion(1, 5, 0);
        var plugin = new ApiVersion(1, 0, 0);
        Assert.True(host.IsCompatibleWith(plugin));
    }

    [Fact]
    public void ApiVersion_IsCompatible_HostOlderThanPlugin_ReturnsFalse()
    {
        var host = new ApiVersion(1, 0, 0);
        var plugin = new ApiVersion(1, 5, 0);
        Assert.False(host.IsCompatibleWith(plugin));
    }

    [Fact]
    public void ApiVersion_IsCompatible_DifferentMajor_ReturnsFalse()
    {
        var v1 = new ApiVersion(1, 0, 0);
        var v2 = new ApiVersion(2, 0, 0);
        Assert.False(v1.IsCompatibleWith(v2));
    }

    [Fact]
    public void HostApiResponse_Success_HasData()
    {
        var r = HostApiResponse<string>.Success("hello");
        Assert.True(r.IsSuccess);
        Assert.Equal("hello", r.Data);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public void HostApiResponse_Failure_HasError()
    {
        var r = HostApiResponse<string>.Failure(ApiErrorCode.NotFound, "not found");
        Assert.False(r.IsSuccess);
        Assert.Equal("not found", r.ErrorMessage);
    }

    [Fact]
    public async Task ManifestReader_ValidManifest_ReturnsSuccess()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "1.0.0", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.True(result.IsSuccess);
        Assert.Equal(ManifestErrorCode.None, result.ErrorCode);
        Assert.Empty(result.Issues);
        Assert.Equal("com.test.app", result.Manifest!.Id);
    }

    [Fact]
    public async Task ManifestReader_MissingFile_ReturnsStableErrorCode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "long-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);

        try
        {
            var result = await ManifestReader.ReadAsync(directory);

            Assert.False(result.IsSuccess);
            Assert.Equal(ManifestErrorCode.FileNotFound, result.ErrorCode);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ManifestReader_InvalidJson_ReturnsStableErrorCode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "long-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{");

        try
        {
            var result = await ManifestReader.ReadAsync(directory);

            Assert.False(result.IsSuccess);
            Assert.Equal(ManifestErrorCode.InvalidJson, result.ErrorCode);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ManifestReader_MissingId_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { version = "1.0.0", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Equal(ManifestErrorCode.ValidationFailed, result.ErrorCode);
        Assert.Contains(result.Issues, issue =>
            issue.Code == ManifestValidationCode.InvalidManifestValue
            && issue.Path == "$");
        Assert.Contains("id", result.Error);
    }

    [Fact]
    public async Task ManifestReader_InvalidVersion_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "abc", name = "Test", entry_point = "test.dll" });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Contains("版本", result.Error);
    }

    [Fact]
    public async Task ManifestReader_UnknownCapability_ReturnsFailure()
    {
        var dir = CreateManifestDir(new { id = "com.test.app", version = "1.0.0", name = "Test", entry_point = "test.dll", capabilities = new[] { "unknown.cap" } });
        var result = await ManifestReader.ReadAsync(dir);
        Assert.False(result.IsSuccess);
        Assert.Contains("未知能力", result.Error);
    }

    [Fact]
    public async Task ManifestReader_WebBackgroundEntryPoint_ReturnsSuccess()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.hybrid",
            version = "1.0.0",
            name = "Hybrid",
            runtime = "webview",
            entry_point = "index.html",
            background = new { entry_point = "Hybrid.Background.dll" },
        });

        try
        {
            var result = await ManifestReader.ReadAsync(dir);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(
                "Hybrid.Background.dll",
                result.Manifest!.Background!.EntryPoint);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(null, "Hybrid.Background.dll")]
    [InlineData("webview", "../Hybrid.Background.dll")]
    [InlineData("webview", "Hybrid.Background.exe")]
    public async Task ManifestReader_InvalidWebBackground_ReturnsFailure(
        string? runtime,
        string backgroundEntryPoint)
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.hybrid",
            version = "1.0.0",
            name = "Hybrid",
            runtime,
            entry_point = "index.html",
            background = new { entry_point = backgroundEntryPoint },
        });

        try
        {
            var result = await ManifestReader.ReadAsync(dir);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue => issue.Path == "background");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ManifestReader_WebWidgetDefinition_ReturnsSuccess()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.widget",
            version = "1.0.0",
            name = "Widget",
            runtime = "webview",
            min_api_version = "1.1.0",
            entry_point = "index.html",
            widgets = new object[]
            {
                new
                {
                    id = "system.status",
                    title = "System Status",
                    description = "Small status card",
                    entry_point = "widgets/status/index.html",
                    icon = "assets/status.png",
                    multiple_instances = true,
                    default_size = new { columns = 4, rows = 2 },
                    min_size = new { columns = 2, rows = 1 },
                    max_size = new { columns = 8, rows = 4 },
                    refresh = new
                    {
                        mode = "interval",
                        interval_seconds = 30,
                        hidden_behavior = "throttle",
                    },
                    appearance = new
                    {
                        transparent = true,
                        host_chrome = false,
                    },
                    settings_schema = new object[]
                    {
                        new
                        {
                            key = "accent",
                            type = "enum",
                            title = "Accent",
                            enum_values = new[] { "auto", "blue" },
                        },
                    },
                },
            },
        });

        try
        {
            var result = await ManifestReader.ReadAsync(dir);

            Assert.True(result.IsSuccess, result.Error);
            var widget = Assert.Single(result.Manifest!.Widgets);
            Assert.Equal("system.status", widget.Id);
            Assert.Equal(4, widget.DefaultSize!.Columns);
            Assert.Equal(PluginWidgetRefreshMode.Interval, widget.Refresh!.Mode);
            Assert.Equal(PluginWidgetHiddenBehavior.Throttle, widget.Refresh.HiddenBehavior);
            Assert.Equal(PluginWidgetSettingType.Enum, widget.SettingsSchema[0].Type);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("native", "1.1.0")]
    [InlineData("webview", null)]
    [InlineData("webview", "1.0.0")]
    public async Task ManifestReader_InvalidWidgetContract_ReturnsFailure(
        string runtime,
        string? minApiVersion)
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.widget",
            version = "1.0.0",
            name = "Widget",
            runtime,
            min_api_version = minApiVersion,
            entry_point = "index.html",
            widgets = new object[]
            {
                new
                {
                    id = "system.status",
                    title = "System Status",
                    entry_point = "widgets/status/index.html",
                    default_size = new { columns = 4, rows = 2 },
                },
            },
        });

        try
        {
            var result = await ManifestReader.ReadAsync(dir);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Issues, issue =>
                issue.Code == ManifestValidationCode.InvalidWidget
                && issue.Path == "widgets");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ManifestReader_InvalidWidgetRefresh_ReturnsFailure()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.widget",
            version = "1.0.0",
            name = "Widget",
            runtime = "webview",
            min_api_version = "1.1.0",
            entry_point = "index.html",
            widgets = new object[]
            {
                new
                {
                    id = "system.status",
                    title = "System Status",
                    entry_point = "widgets/status/index.html",
                    default_size = new { columns = 4, rows = 2 },
                    refresh = new
                    {
                        mode = "manual",
                        interval_seconds = 30,
                    },
                },
            },
        });

        try
        {
            var result = await ManifestReader.ReadAsync(dir);

            Assert.False(result.IsSuccess);
            Assert.Contains("interval_seconds", result.Error);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("network.ports")]
    [InlineData("system.performance")]
    [InlineData("filesystem.advanced")]
    [InlineData("system.power")]
    [InlineData("system.wallpaper")]
    [InlineData("display.brightness")]
    [InlineData("system.input")]
    [InlineData("system.cache")]
    [InlineData("system.schedule")]
    [InlineData("ui.window")]
    [InlineData("text.pinyin")]
    [InlineData("window.info")]
    public async Task ManifestReader_CurrentHostCapability_ReturnsSuccess(string capability)
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.capability",
            version = "1.0.0",
            name = "Capability Test",
            entry_point = "index.html",
            capabilities = new[] { capability }
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void StandaloneScriptCapabilities_ValidDirectives_AreExtracted()
    {
        var source = """
            // @capabilities system.hotkey, system.notification
            // @capability network.http
            // @capability unknown.capability
            console.log('ready');
            """;

        var capabilities = StandalonePluginLoader.ExtractCapabilities(source);

        Assert.Equal(
            new[] { "network.http", "system.hotkey", "system.notification" },
            capabilities);
    }

    [Fact]
    public void CapabilityMetadata_AllManifestCapabilitiesHaveDescriptions()
    {
        var missing = ManifestReader.KnownCapabilities
            .Where(capability => CapabilityMetadata.GetInfo(capability).Level == SecurityLevel.Unknown)
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("process.start", "system.process")]
    [InlineData("performance.getCpuUsage", "system.performance")]
    [InlineData("networkPort.getTcpListeners", "network.ports")]
    [InlineData("audio.setVolume", "system.audio")]
    [InlineData("power.shutdown", "system.power")]
    [InlineData("wallpaper.set", "system.wallpaper")]
    [InlineData("brightness.set", "display.brightness")]
    [InlineData("fileSystem.enumerate", "filesystem.advanced")]
    [InlineData("fileSystem.planOrganization", "filesystem.advanced")]
    [InlineData("fileSystem.executeOrganization", "filesystem.advanced")]
    [InlineData("cache.cleanTemp", "system.cache")]
    [InlineData("schedule.create", "system.schedule")]
    [InlineData("input.keyPress", "system.input")]
    [InlineData("ui.confirm", "ui.window")]
    public void WebBridgeMethod_RequiresExpectedCapability(string method, string capability)
    {
        Assert.Equal(capability, WebPluginBridgeProtocol.GetRequiredCapability(method));
    }

    [Fact]
    public void WebBridgeScript_ContainsPlatformApisAndPromiseResolution()
    {
        var script = WebPluginBridgeProtocol.BuildInjectionScript("com.test.bridge");

        Assert.Contains("process:", script);
        Assert.Contains("performance:", script);
        Assert.Contains("networkPort:", script);
        Assert.Contains("audio:", script);
        Assert.Contains("power:", script);
        Assert.Contains("fileSystem:", script);
        Assert.Contains("planOrganization: function", script);
        Assert.Contains("executeOrganization: function", script);
        Assert.Contains("cache:", script);
        Assert.Contains("schedule:", script);
        Assert.Contains("input:", script);
        Assert.Contains("confirm: function", script);
        Assert.Contains("_pending[m.id].resolve", script);
        Assert.Contains("startMonitoring: function(callback)", script);
        Assert.Contains("compareExchange: function(k,e,v)", script);
        Assert.Contains("m.type==='clipboard.changed'", script);
        Assert.Contains("com.test.bridge", script);
    }

    [Fact]
    public void WebPluginArguments_NullableString_PreservesJsonNull()
    {
        using var document = JsonDocument.Parse("null");
        object?[] arguments = [document.RootElement.Clone()];

        Assert.Null(WebPluginArguments.GetNullableString(arguments, 0));
        Assert.Null(WebPluginArguments.GetNullableString([], 0));
        Assert.Equal(
            "expected",
            WebPluginArguments.GetNullableString(["expected"], 0));
    }

    [Fact]
    public void WebBridgeProtocol_ParsesRequestsCaseInsensitively()
    {
        var request = WebPluginBridgeProtocol.ParseRequest(
            "{\"ID\":17,\"METHOD\":\"clipboard.setText\",\"ARGS\":[\"hello\"]}");

        Assert.NotNull(request);
        Assert.Equal(17, request.Id);
        Assert.Equal("clipboard.setText", request.Method);
        Assert.Single(request.Args);
        Assert.Equal("hello", Assert.IsType<JsonElement>(request.Args[0]).GetString());
    }

    [Fact]
    public void WebBridgeProtocol_RoundTripsStructuredCommandResult()
    {
        var message = WebPluginBridgeProtocol.ParseCommandResult(
            """
            {"type":"long.command-result","request_id":"request-1","success":true,
             "message":"done","outputs":{"result":{"type":"text","value":"encoded"}}}
            """);

        Assert.NotNull(message);
        Assert.True(WebPluginBridgeProtocol.TryCreateCommandResult(
            message!,
            out var result,
            out var error));
        Assert.Null(error);
        Assert.True(result.IsSuccess);
        Assert.Equal("encoded", result.Outputs["result"].Value);
        Assert.Equal(PluginCommandOutputType.Text, result.Outputs["result"].Type);
    }

    [Fact]
    public void WebBridgeProtocol_RejectsInvalidStructuredOutput()
    {
        var message = WebPluginBridgeProtocol.ParseCommandResult(
            """
            {"type":"long.command-result","request_id":"request-1","success":true,
             "outputs":{"result":{"type":"binary","value":"data"}}}
            """);

        Assert.NotNull(message);
        Assert.False(WebPluginBridgeProtocol.TryCreateCommandResult(
            message!,
            out var result,
            out var error));
        Assert.False(result.IsSuccess);
        Assert.Contains("invalid", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebCommandCoordinator_CorrelatesResultWithPendingRequest()
    {
        string? sent = null;
        using var coordinator = new WebPluginCommandCoordinator(
            "com.test.web",
            message =>
            {
                sent = message;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));

        var pending = coordinator.SendAsync(
            new PluginCommandInvocation { CommandId = "transform" },
            CancellationToken.None);
        await Task.Yield();
        Assert.NotNull(sent);
        using var document = System.Text.Json.JsonDocument.Parse(sent!);
        var requestId = document.RootElement.GetProperty("request_id").GetString();

        var response = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "long.command-result",
            request_id = requestId,
            success = true,
            outputs = new { },
        });
        Assert.True(coordinator.TryHandle(response));
        Assert.True((await pending).IsSuccess);
    }

    [Fact]
    public void WebBridgeProtocol_SerializesLowercaseResponseAndEventContracts()
    {
        using var success = JsonDocument.Parse(
            WebPluginBridgeProtocol.SerializeResult(9, new { success = true }));
        using var failure = JsonDocument.Parse(
            WebPluginBridgeProtocol.SerializeError(10, "denied"));
        using var hotkey = JsonDocument.Parse(
            WebPluginBridgeProtocol.SerializeHotkey("Alt+Space"));
        using var clipboard = JsonDocument.Parse(
            WebPluginBridgeProtocol.SerializeClipboardChanged(new ClipboardChangedEventArgs
            {
                Text = "copied",
                ContentType = ClipboardContentType.Text,
                Timestamp = new DateTime(2026, 7, 23, 12, 30, 0, DateTimeKind.Local),
            }));

        Assert.Equal(9, success.RootElement.GetProperty("id").GetInt32());
        Assert.True(success.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.False(success.RootElement.TryGetProperty("error", out _));
        Assert.Equal("denied", failure.RootElement.GetProperty("error").GetString());
        Assert.Equal("hotkey", hotkey.RootElement.GetProperty("type").GetString());
        Assert.Equal("Alt+Space", hotkey.RootElement.GetProperty("hotkey").GetString());
        Assert.Equal("clipboard.changed", clipboard.RootElement.GetProperty("type").GetString());
        Assert.Equal("text", clipboard.RootElement.GetProperty("content_type").GetString());
        Assert.Equal("copied", clipboard.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void WebPluginArguments_ConvertsJsonPrimitivesAndUsesDefaults()
    {
        using var document = JsonDocument.Parse("[42,true,\"17\",\"invalid\"]");
        var args = document.RootElement.EnumerateArray()
            .Select(element => (object?)element.Clone())
            .ToArray();

        Assert.Equal(42, WebPluginArguments.GetInt(args, 0));
        Assert.True(WebPluginArguments.GetBool(args, 1));
        Assert.Equal(17L, WebPluginArguments.GetLong(args, 2));
        Assert.Equal(9, WebPluginArguments.GetInt(args, 3, 9));
        Assert.Equal("fallback", WebPluginArguments.GetString(args, 8, "fallback"));
    }

    [Fact]
    public void WebPluginArguments_DeserializesStructuredCollectionsAndHeaders()
    {
        using var document = JsonDocument.Parse(
            "[[\"txt\",\"md\"],{\"Authorization\":\"Bearer token\"}]");
        var args = document.RootElement.EnumerateArray()
            .Select(element => (object?)element.Clone())
            .ToArray();

        Assert.Equal(new[] { "txt", "md" }, WebPluginArguments.GetStringList(args, 0));
        var headers = WebPluginArguments.GetHeaders(args, 1);
        Assert.NotNull(headers);
        Assert.Equal("Bearer token", headers["Authorization"]);
        Assert.Null(WebPluginArguments.GetHeaders(new object?[] { "not-json" }, 0));
    }

    [Fact]
    public void PluginRegistry_Register_AddsPlugin()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", Name = "Test", Version = "1.0.0", EntryPoint = "t.dll" };
        var plugin = new TestPlugin();
        Assert.True(reg.Register(manifest, plugin, null, "/test"));
        Assert.NotNull(reg.Get("test"));
        Assert.Single(reg.GetAll());
    }

    [Fact]
    public void PluginRegistry_DuplicateRegister_ReturnsFalse()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", Name = "Test", Version = "1.0.0", EntryPoint = "t.dll" };
        var plugin = new TestPlugin();
        reg.Register(manifest, plugin, null, "/test");
        Assert.False(reg.Register(manifest, plugin, null, "/test"));
    }

    [Fact]
    public void PluginRegistry_HasCapability_ChecksCorrectly()
    {
        var reg = new PluginRegistry();
        var manifest = new PluginManifest { Id = "test", EntryPoint = "t.dll", Capabilities = new List<string> { "system.hotkey" } };
        reg.Register(manifest, new TestPlugin(), null, "/test");
        Assert.True(reg.HasCapability("test", "system.hotkey"));
        Assert.False(reg.HasCapability("test", "shell.selection"));
    }

    [Fact]
    public void PluginRegistry_Get_NotFound_ReturnsNull()
    {
        var reg = new PluginRegistry();
        Assert.Null(reg.Get("nonexistent"));
    }

    // ===== helpers =====

    private static string CreateManifestDir(object content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_manifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(content));
        return dir;
    }

    private class TestPlugin : ILongPlugin
    {
        public string Id => "test";
        public string Name => "Test";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }
}
