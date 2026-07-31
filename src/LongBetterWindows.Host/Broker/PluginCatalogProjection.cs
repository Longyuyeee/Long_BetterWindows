using LongBetterWindows.Host.Engine;
using LongBetterWindows.PluginIpc.Contracts;

namespace LongBetterWindows.Host.Broker;

internal sealed class PluginCatalogProjection(PluginRegistry registry)
{
    public PluginCatalogListResponse List(PluginCatalogListRequest request)
    {
        var revision = registry.CatalogRevision;
        if (request.IfRevision == revision)
            return new PluginCatalogListResponse(revision, true, Array.Empty<PluginCatalogItem>());

        var plugins = registry.GetAll()
            .OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(Project)
            .ToArray();
        return new PluginCatalogListResponse(revision, false, plugins);
    }

    public PluginCatalogGetResponse? Get(PluginCatalogGetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
            return null;
        var entry = registry.Get(request.PluginId);
        return entry is null
            ? null
            : new PluginCatalogGetResponse(registry.CatalogRevision, Project(entry));
    }

    private static PluginCatalogItem Project(PluginEntry entry)
    {
        var manifest = entry.Manifest;
        return new PluginCatalogItem(
            entry.Id,
            entry.DisplayName,
            manifest.Description,
            manifest.Author,
            manifest.Version,
            string.IsNullOrWhiteSpace(manifest.Runtime) ? "native" : manifest.Runtime,
            entry.State.ToString().ToLowerInvariant(),
            manifest.MinApiVersion,
            manifest.MinHostVersion,
            manifest.Commands.Select(command => new PluginCatalogCommand(
                command.Id,
                command.Title,
                command.Description,
                command.AcceptedInputs.Select(input => input.ToString().ToLowerInvariant()).ToArray()))
                .ToArray(),
            manifest.Widgets.Select(widget => new PluginCatalogWidget(
                widget.Id,
                widget.Title,
                widget.Description,
                widget.MultipleInstances)).ToArray());
    }
}
