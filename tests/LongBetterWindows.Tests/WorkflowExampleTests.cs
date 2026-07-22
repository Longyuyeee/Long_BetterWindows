using System.IO;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class WorkflowExampleTests
{
    [Fact]
    public async Task Base64RoundTrip_UsesRealManifestAndPassesPreflight()
    {
        var root = FindRepositoryRoot();
        var manifestResult = await ManifestReader.ReadAsync(
            Path.Combine(root, "src", "Base64Tool"));
        Assert.True(manifestResult.IsSuccess, manifestResult.Error);

        var registry = new PluginRegistry();
        Assert.True(registry.Register(
            manifestResult.Manifest!,
            new object(),
            null,
            Path.Combine(root, "src", "Base64Tool")));
        var json = await File.ReadAllTextAsync(Path.Combine(
            root,
            "examples",
            "workflows",
            "base64-roundtrip.workflow.json"));

        var document = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: false);

        Assert.True(document.IsSuccess, document.Error);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, document.TrustLevel);
        Assert.Equal(3, CommandWorkflowDocumentCodec.CurrentSchemaVersion);
        var preflight = new CommandWorkflowPlanner(registry).Preflight(document.Workflow!);
        Assert.True(preflight.IsValid, string.Join(Environment.NewLine, preflight.Issues));
        var binding = Assert.Single(document.Workflow!.Steps[1].Command!.Bindings!);
        Assert.Equal("encode", binding.SourceStepId);
        Assert.Equal("result", binding.OutputKey);
        Assert.Equal(WorkflowBindingTarget.Text, binding.Target);
    }

    [Fact]
    public async Task JsonUrlRoundTrip_UsesRealManifestsAndPassesPreflight()
    {
        var root = FindRepositoryRoot();
        var registry = new PluginRegistry();
        await RegisterManifestAsync(registry, root, "JsonFormatterPlugin");
        await RegisterManifestAsync(registry, root, "UrlToolkit");
        var json = await File.ReadAllTextAsync(Path.Combine(
            root,
            "examples",
            "workflows",
            "json-url-roundtrip.workflow.json"));

        var document = CommandWorkflowDocumentCodec.Deserialize(json, isManagedFile: false);

        Assert.True(document.IsSuccess, document.Error);
        Assert.Equal(WorkflowDocumentTrustLevel.Untrusted, document.TrustLevel);
        var workflow = document.Workflow!;
        Assert.Equal(4, workflow.Steps.Count);
        var preflight = new CommandWorkflowPlanner(registry).Preflight(workflow);
        Assert.True(preflight.IsValid, string.Join(Environment.NewLine, preflight.Issues));
        Assert.Equal(
            new[] { "minify-json", "encode-url", "decode-url" },
            workflow.Steps.Skip(1)
                .Select(step => Assert.Single(step.Command!.Bindings!).SourceStepId));
        Assert.All(
            workflow.Steps.Skip(1),
            step => Assert.Equal("result", Assert.Single(step.Command!.Bindings!).OutputKey));
    }

    private static async Task RegisterManifestAsync(
        PluginRegistry registry,
        string root,
        string pluginDirectoryName)
    {
        var directory = Path.Combine(root, "src", pluginDirectoryName);
        var result = await ManifestReader.ReadAsync(directory);
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(registry.Register(result.Manifest!, new object(), null, directory));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
