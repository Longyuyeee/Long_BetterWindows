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
