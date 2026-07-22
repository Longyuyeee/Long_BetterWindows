using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public class CommandContractTests
{
    [Fact]
    public void PluginState_LegacyBinaryValuesRemainStable()
    {
        Assert.Equal(0, (int)PluginState.Loaded);
        Assert.Equal(1, (int)PluginState.Running);
        Assert.Equal(2, (int)PluginState.Error);
        Assert.Equal(3, (int)PluginState.Disabled);
        Assert.Equal(PluginState.Stopped, PluginState.Disabled);
        Assert.Equal(4, (int)PluginState.Background);
    }

    [Fact]
    public async Task ManifestReader_LegacyManifest_RemainsCompatible()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.legacy",
            version = "1.0.0",
            name = "Legacy",
            entry_point = "legacy.dll",
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Manifest!.Commands);
        Assert.Null(result.Manifest.Window);
        Assert.Null(result.Manifest.Lifecycle);
    }

    [Fact]
    public async Task ManifestReader_CommandAndWindowContract_ParsesSnakeCaseEnums()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.translate",
            version = "1.0.0",
            name = "Translate",
            entry_point = "translate.dll",
            commands = new[]
            {
                new
                {
                    id = "translate.text",
                    title = "翻译文本",
                    aliases = new[] { "翻译", "fy" },
                    accepted_inputs = new[] { "text", "clipboard" },
                    view_mode = "form",
                    keep_alive = true,
                    priority = 20,
                    outputs = new[]
                    {
                        new { key = "translated-text", type = "text", description = "Translated text" },
                    },
                },
            },
            window = new
            {
                mode = "standard",
                preferred_width = 680,
                preferred_height = 520,
                min_width = 480,
                min_height = 360,
            },
            lifecycle = new
            {
                start_with_host = false,
                close_behavior = "background",
                default_presentation = "embedded",
                search_in_background = true,
            },
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.True(result.IsSuccess, result.Error);
        var command = Assert.Single(result.Manifest!.Commands);
        Assert.Equal(AcceptedInputType.Text, command.AcceptedInputs[0]);
        Assert.Equal(AcceptedInputType.Clipboard, command.AcceptedInputs[1]);
        var output = Assert.Single(command.Outputs);
        Assert.Equal("translated-text", output.Key);
        Assert.Equal(PluginCommandOutputType.Text, output.Type);
        Assert.Equal(PluginViewMode.Form, command.ViewMode);
        Assert.True(command.KeepAlive);
        Assert.Equal(PluginWindowMode.Standard, result.Manifest.Window!.Mode);
        Assert.Equal(680, result.Manifest.Window.PreferredWidth);
        Assert.False(result.Manifest.Lifecycle!.StartWithHost);
        Assert.Equal(PluginCloseBehavior.Background, result.Manifest.Lifecycle.CloseBehavior);
        Assert.Equal(PluginPresentationMode.Embedded, result.Manifest.Lifecycle.DefaultPresentation);
        Assert.True(result.Manifest.Lifecycle.SearchInBackground);
    }

    [Fact]
    public async Task ManifestReader_DuplicateCommandIds_ReturnsFailure()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.duplicate",
            version = "1.0.0",
            name = "Duplicate",
            entry_point = "duplicate.dll",
            commands = new[]
            {
                new { id = "open", title = "Open", accepted_inputs = new[] { "none" } },
                new { id = "OPEN", title = "Open again", accepted_inputs = new[] { "none" } },
            },
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.False(result.IsSuccess);
        Assert.Contains("OPEN", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManifestReader_InvalidAndDuplicateOutputKeysReturnFailure()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.outputs",
            version = "1.0.0",
            name = "Outputs",
            entry_point = "outputs.dll",
            commands = new[]
            {
                new
                {
                    id = "produce",
                    title = "Produce",
                    accepted_inputs = new[] { "none" },
                    outputs = new[]
                    {
                        new { key = "same", type = "text" },
                        new { key = "same", type = "path" },
                        new { key = "not valid", type = "text" },
                    },
                },
            },
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.False(result.IsSuccess);
        Assert.Contains("重复 output key", result.Error);
        Assert.Contains("output key 无效", result.Error);
    }

    [Fact]
    public async Task ManifestReader_PreferredWindowSmallerThanMinimum_ReturnsFailure()
    {
        var dir = CreateManifestDir(new
        {
            id = "com.test.window",
            version = "1.0.0",
            name = "Window",
            entry_point = "window.dll",
            window = new { preferred_width = 400, min_width = 480 },
        });

        var result = await ManifestReader.ReadAsync(dir);

        Assert.False(result.IsSuccess);
        Assert.Contains("preferred_width", result.Error);
    }

    [Theory]
    [InlineData("QuickLaunchPlugin")]
    [InlineData("TranslatePlugin")]
    [InlineData("ScreenshotPlugin")]
    [InlineData("ColorPickerPlugin")]
    [InlineData("FolderNotePlugin")]
    [InlineData("FileRenamerPlugin")]
    public async Task BuiltInManifest_CommandContract_IsValid(string pluginDirectory)
    {
        var repositoryRoot = FindRepositoryRoot();
        var directory = Path.Combine(repositoryRoot, "src", pluginDirectory);

        var result = await ManifestReader.ReadAsync(directory);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotEmpty(result.Manifest!.Commands);
        Assert.NotNull(result.Manifest.Window);
    }

    [Fact]
    public void CommandRegistry_ExactMatchRanksAbovePartialMatch()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(CreateManifest(
            "translate",
            new PluginCommand
            {
                Id = "translate.text",
                Title = "翻译",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Text },
            },
            new PluginCommand
            {
                Id = "translate.document",
                Title = "翻译文档",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.File },
            }));

        var results = registry.Search("翻译");

        Assert.Equal(2, results.Count);
        Assert.Equal("translate.text", results[0].Descriptor.Command.Id);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void CommandRegistry_InputTypeFiltersIncompatibleCommands()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(CreateManifest(
            "tools",
            new PluginCommand
            {
                Id = "open.file",
                Title = "打开",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.File },
            },
            new PluginCommand
            {
                Id = "open.url",
                Title = "打开",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Url },
            }));

        var results = registry.Search("打开", AcceptedInputType.File);

        var result = Assert.Single(results);
        Assert.Equal("open.file", result.Descriptor.Command.Id);
    }

    [Fact]
    public async Task CommandRegistry_DeclaredPinyinAlias_IsSearchable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestResult = await ManifestReader.ReadAsync(
            Path.Combine(repositoryRoot, "src", "TranslatePlugin"));
        var registry = new CommandRegistry();
        registry.RegisterManifest(manifestResult.Manifest!);

        var byFullPinyin = Assert.Single(registry.Search("fanyi"));
        var byInitials = Assert.Single(registry.Search("fy"));

        Assert.Equal("translate.text", byFullPinyin.Descriptor.Command.Id);
        Assert.Equal("translate.text", byInitials.Descriptor.Command.Id);
    }

    [Fact]
    public void PluginRegistry_RegisterAndUnregister_SynchronizesCommands()
    {
        var registry = new PluginRegistry();
        var manifest = CreateManifest(
            "test",
            new PluginCommand
            {
                Id = "test.open",
                Title = "Open",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.None },
            });

        Assert.True(registry.Register(manifest, new TestPlugin(), null, "/test"));
        Assert.Single(registry.Commands.GetAll());

        Assert.True(registry.Unregister("test"));
        Assert.Empty(registry.Commands.GetAll());
    }

    [Fact]
    public async Task PluginRegistry_EnforcesLifecycleTransitionsAndResumesBackgroundInstance()
    {
        var registry = new PluginRegistry();
        var plugin = new BackgroundLifecyclePlugin();
        var manifest = new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "lifecycle.dll",
            Lifecycle = new PluginLifecyclePreference
            {
                CloseBehavior = PluginCloseBehavior.Background,
                SearchInBackground = true,
            },
        };
        Assert.True(registry.Register(manifest, plugin, null, "/lifecycle"));

        Assert.False(registry.SetState(plugin.Id, PluginState.Background));
        Assert.Equal(PluginState.Loaded, registry.Get(plugin.Id)!.State);

        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.Equal(PluginState.Running, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.StartCount);

        Assert.True(await registry.HandleWindowClosedAsync(plugin.Id));
        Assert.Equal(PluginState.Background, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.BackgroundCount);

        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.Equal(PluginState.Running, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.StartCount);
        Assert.Equal(1, plugin.ResumeCount);

        Assert.True(await registry.StopPluginAsync(plugin.Id));
        Assert.Equal(PluginState.Stopped, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.StopCount);
        Assert.False(await registry.MoveToBackgroundAsync(plugin.Id));
    }

    [Fact]
    public async Task PluginRegistry_LegacyDefaultCloseBehaviorStopsPlugin()
    {
        var registry = new PluginRegistry();
        var plugin = new BackgroundLifecyclePlugin();
        Assert.True(registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "lifecycle.dll",
        }, plugin, null, "/lifecycle-default"));

        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.True(await registry.HandleWindowClosedAsync(plugin.Id));

        Assert.Equal(PluginState.Stopped, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.StopCount);
        Assert.Equal(0, plugin.BackgroundCount);
    }

    [Fact]
    public async Task WindowCloseStopsWithoutPersistentlyDisablingPlugin()
    {
        var registry = new PluginRegistry();
        var plugin = new BackgroundLifecyclePlugin();
        Assert.True(registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "lifecycle.dll",
            DefaultSettings = new Dictionary<string, object>
            {
                ["auto_start"] = true,
            },
        }, plugin, null, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.True(await registry.HandleWindowClosedAsync(plugin.Id));

        Assert.Equal("true", registry.Get(plugin.Id)!.GetSetting("auto_start"));
    }

    [Fact]
    public async Task PluginRegistry_FailedStopDoesNotPublishStoppedState()
    {
        var registry = new PluginRegistry();
        var plugin = new BackgroundLifecyclePlugin { StopSucceeds = false };
        Assert.True(registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "lifecycle.dll",
        }, plugin, null, "/lifecycle-stop-failure"));
        Assert.True(await registry.StartPluginAsync(plugin.Id));

        Assert.False(await registry.StopPluginAsync(plugin.Id));

        Assert.Equal(PluginState.Running, registry.Get(plugin.Id)!.State);
        Assert.Equal(1, plugin.StopCount);
    }

    [Fact]
    public async Task PluginRegistry_StopReleasesPluginAndHostOwnedResources()
    {
        var releasedPluginId = string.Empty;
        var registry = new PluginRegistry();
        registry.AttachHostResourceReleaser(pluginId =>
        {
            releasedPluginId = pluginId;
            return Task.CompletedTask;
        });
        var plugin = new ResourceLifecyclePlugin();
        Assert.True(registry.Register(new PluginManifest
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            EntryPoint = "resources.dll",
        }, plugin, null, "/resources"));

        Assert.True(await registry.StartPluginAsync(plugin.Id));
        Assert.True(await registry.StopPluginAsync(plugin.Id));

        Assert.Equal(1, plugin.ReleaseCount);
        Assert.Equal(plugin.Id, releasedPluginId);
        Assert.Equal(PluginState.Stopped, registry.Get(plugin.Id)!.State);
    }

    [Fact]
    public async Task CommandExecutor_CommandHandler_ReceivesNormalizedInvocation()
    {
        var registry = new PluginRegistry();
        var manifest = CreateManifest(
            "handler",
            new PluginCommand
            {
                Id = "handler.open",
                Title = "Open",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Text },
            });
        var plugin = new CommandTestPlugin();
        registry.Register(manifest, plugin, null, "/handler");
        var executor = new CommandExecutor(registry);
        var invocation = new PluginCommandInvocation
        {
            CommandId = "handler.open",
            InputType = AcceptedInputType.Text,
            Text = "hello",
        };

        var result = await executor.ExecuteAsync("handler:handler.open", invocation);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("hello", plugin.LastInvocation!.Text);
        Assert.Equal(PluginState.Running, registry.Get("handler")!.State);
    }

    [Fact]
    public async Task CommandExecutor_IncompatibleInput_ReturnsFailureWithoutCallingPlugin()
    {
        var registry = new PluginRegistry();
        var manifest = CreateManifest(
            "handler",
            new PluginCommand
            {
                Id = "handler.open",
                Title = "Open",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.File },
            });
        var plugin = new CommandTestPlugin();
        registry.Register(manifest, plugin, null, "/handler");
        var executor = new CommandExecutor(registry);

        var result = await executor.ExecuteAsync(
            "handler:handler.open",
            new PluginCommandInvocation
            {
                CommandId = "handler.open",
                InputType = AcceptedInputType.Url,
                Text = "https://example.test",
            });

        Assert.False(result.IsSuccess);
        Assert.Null(plugin.LastInvocation);
    }

    [Fact]
    public void CommandRegistry_EmptyQueryWithClipboardContext_RecommendsClipboardCommand()
    {
        var registry = new CommandRegistry();
        registry.RegisterManifest(CreateManifest(
            "translate",
            new PluginCommand
            {
                Id = "translate.text",
                Title = "翻译文本",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Clipboard },
            }));

        var result = Assert.Single(registry.Search(string.Empty, AcceptedInputType.Clipboard));

        Assert.Equal("translate.text", result.Descriptor.Command.Id);
    }

    [Fact]
    public void CommandInvocationFactory_SelectsOnlyBestDeclaredContext()
    {
        var descriptor = new CommandDescriptor(
            "image:open",
            "image",
            "Image",
            new PluginCommand
            {
                Id = "open",
                Title = "Open image",
                AcceptedInputs = new List<AcceptedInputType> { AcceptedInputType.Image },
            });
        var snapshot = new ContextSnapshot(DateTimeOffset.UtcNow, new[]
        {
            new ContextItem
            {
                Id = "text",
                Source = ContextSource.Clipboard,
                Label = "Text",
                Text = "private text",
                CompatibleInputTypes = new[] { AcceptedInputType.Text },
            },
            new ContextItem
            {
                Id = "image",
                Source = ContextSource.Clipboard,
                Label = "Image",
                ImagePng = new byte[] { 1, 2, 3 },
                Paths = new[] { "ignored-path" },
                CompatibleInputTypes = new[] { AcceptedInputType.Image },
            },
        });

        var invocation = CommandInvocationFactory.Create(descriptor, snapshot);

        Assert.Equal("open", invocation.CommandId);
        Assert.Equal(AcceptedInputType.Image, invocation.InputType);
        Assert.Equal(new byte[] { 1, 2, 3 }, invocation.ImagePng);
        Assert.Null(invocation.Text);
        Assert.Equal(new[] { "ignored-path" }, invocation.Paths);
    }

    private static PluginManifest CreateManifest(string id, params PluginCommand[] commands)
        => new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            EntryPoint = id + ".dll",
            Commands = commands.ToList(),
        };

    private static string CreateManifestDir(object content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"test_manifest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(content));
        return dir;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TestPlugin : ILongPlugin
    {
        public string Id => "test";
        public string Name => "Test";
        public string Version => "1.0.0";
        public PluginState State => PluginState.Loaded;
        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);
        public Task<bool> StartAsync() => Task.FromResult(true);
        public Task<bool> StopAsync() => Task.FromResult(true);
    }

    private sealed class CommandTestPlugin : ILongPlugin, IPluginCommandHandler
    {
        public string Id => "handler";
        public string Name => "Handler";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public PluginCommandInvocation? LastInvocation { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);

        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task<PluginCommandResult> ExecuteCommandAsync(
            PluginCommandInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            LastInvocation = invocation;
            return Task.FromResult(PluginCommandResult.Success());
        }
    }

    private sealed class BackgroundLifecyclePlugin : ILongPlugin, IPluginBackgroundLifecycle
    {
        public string Id => "lifecycle";
        public string Name => "Lifecycle";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int BackgroundCount { get; private set; }
        public int ResumeCount { get; private set; }
        public bool StopSucceeds { get; init; } = true;

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);

        public Task<bool> StartAsync()
        {
            StartCount++;
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            StopCount++;
            if (StopSucceeds)
                State = PluginState.Stopped;
            return Task.FromResult(StopSucceeds);
        }

        public Task<bool> EnterBackgroundAsync()
        {
            BackgroundCount++;
            State = PluginState.Background;
            return Task.FromResult(true);
        }

        public Task<bool> ResumeAsync()
        {
            ResumeCount++;
            State = PluginState.Running;
            return Task.FromResult(true);
        }
    }

    private sealed class ResourceLifecyclePlugin : ILongPlugin, IPluginResourceLifecycle
    {
        public string Id => "resources";
        public string Name => "Resources";
        public string Version => "1.0.0";
        public PluginState State { get; private set; } = PluginState.Loaded;
        public int ReleaseCount { get; private set; }

        public Task<bool> InitializeAsync(IHostApi host) => Task.FromResult(true);

        public Task<bool> StartAsync()
        {
            State = PluginState.Running;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            State = PluginState.Stopped;
            return Task.FromResult(true);
        }

        public Task ReleaseResourcesAsync()
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }
}
