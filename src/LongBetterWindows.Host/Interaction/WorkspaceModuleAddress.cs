using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Host.Interaction
{
    internal enum WorkspaceModuleAddressKind
    {
        Management,
        Marketplace,
        Settings,
        Diagnostics,
        Developer,
        Workflow,
        PluginSettings,
    }

    internal readonly record struct WorkspaceModuleAddress(
        WorkspaceModuleAddressKind Kind,
        string ResourceId)
    {
        public string CanonicalValue => $"{GetKindName(Kind)}:{ResourceId}";

        public static bool TryParse(
            string? value,
            out WorkspaceModuleAddress address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var candidate = value.Trim();
            var separator = candidate.IndexOf(':');
            if (separator <= 0
                || separator == candidate.Length - 1
                || candidate.IndexOf(':', separator + 1) >= 0)
            {
                return false;
            }

            var kindText = candidate[..separator];
            var resourceId = candidate[(separator + 1)..];
            if (!TryParseKind(kindText, out var kind)
                || !IsAllowedResource(kind, resourceId))
            {
                return false;
            }

            if (kind is not WorkspaceModuleAddressKind.Workflow
                and not WorkspaceModuleAddressKind.PluginSettings)
            {
                resourceId = resourceId.ToLowerInvariant();
            }
            address = new WorkspaceModuleAddress(kind, resourceId);
            return true;
        }

        private static bool TryParseKind(
            string value,
            out WorkspaceModuleAddressKind kind)
        {
            kind = value.ToLowerInvariant() switch
            {
                "management" => WorkspaceModuleAddressKind.Management,
                "marketplace" => WorkspaceModuleAddressKind.Marketplace,
                "settings" => WorkspaceModuleAddressKind.Settings,
                "diagnostics" => WorkspaceModuleAddressKind.Diagnostics,
                "developer" => WorkspaceModuleAddressKind.Developer,
                "workflow" => WorkspaceModuleAddressKind.Workflow,
                "plugin-settings" => WorkspaceModuleAddressKind.PluginSettings,
                _ => default,
            };
            return GetKindName(kind).Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedResource(
            WorkspaceModuleAddressKind kind,
            string resourceId)
        {
            var fixedResource = kind switch
            {
                WorkspaceModuleAddressKind.Management => "root",
                WorkspaceModuleAddressKind.Marketplace => "catalog",
                WorkspaceModuleAddressKind.Settings => "root",
                WorkspaceModuleAddressKind.Diagnostics => "root",
                WorkspaceModuleAddressKind.Developer => "root",
                _ => null,
            };
            if (fixedResource is not null)
            {
                return fixedResource.Equals(
                    resourceId,
                    StringComparison.OrdinalIgnoreCase);
            }

            return resourceId.Length <= 128
                && resourceId.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');
        }

        private static string GetKindName(WorkspaceModuleAddressKind kind)
            => kind switch
            {
                WorkspaceModuleAddressKind.Management => "management",
                WorkspaceModuleAddressKind.Marketplace => "marketplace",
                WorkspaceModuleAddressKind.Settings => "settings",
                WorkspaceModuleAddressKind.Diagnostics => "diagnostics",
                WorkspaceModuleAddressKind.Developer => "developer",
                WorkspaceModuleAddressKind.Workflow => "workflow",
                WorkspaceModuleAddressKind.PluginSettings => "plugin-settings",
                _ => string.Empty,
            };
    }

    internal enum WorkspaceModuleResolutionError
    {
        None,
        InvalidAddress,
        ResourceNotFound,
        ResourceUnsupported,
    }

    internal sealed record WorkspaceModuleResolution(
        WorkspaceModuleAddress Address,
        WorkspaceModuleDescriptor? Module,
        WorkspaceModuleResolutionError Error)
    {
        public bool IsSuccess => Module is not null
            && Error == WorkspaceModuleResolutionError.None;
    }

    internal sealed class WorkspaceModuleResolver
    {
        private readonly PluginRegistry _plugins;
        private readonly CommandWorkflowRepository _workflows;
        private readonly Func<string, string>? _localize;

        public WorkspaceModuleResolver(
            PluginRegistry plugins,
            CommandWorkflowRepository workflows,
            Func<string, string>? localize = null)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
            _localize = localize;
        }

        public async Task<WorkspaceModuleResolution> ResolveAsync(
            WorkspaceModuleAddress address,
            CancellationToken cancellationToken = default)
        {
            if (!WorkspaceModuleAddress.TryParse(
                address.CanonicalValue,
                out var canonical))
            {
                return Failure(address, WorkspaceModuleResolutionError.InvalidAddress);
            }

            switch (canonical.Kind)
            {
                case WorkspaceModuleAddressKind.Management:
                    return Success(canonical, Text("page.overview.title", "管理中心"), canClose: false);
                case WorkspaceModuleAddressKind.Marketplace:
                    return Success(canonical, Text("page.market.title", "插件应用市场"));
                case WorkspaceModuleAddressKind.Settings:
                    return Success(canonical, Text("page.settings.title", "设置"));
                case WorkspaceModuleAddressKind.Diagnostics:
                    return Success(canonical, Text("page.diagnostics.title", "诊断"));
                case WorkspaceModuleAddressKind.Developer:
                    return Success(canonical, Text("page.developer.title", "开发者"));
                case WorkspaceModuleAddressKind.Workflow:
                    var workflow = await _workflows.LoadManagedAsync(
                        canonical.ResourceId,
                        cancellationToken);
                    return workflow.IsSuccess
                        ? Success(
                            canonical,
                            workflow.Workflow?.Name ?? canonical.ResourceId,
                            searchScopeId: "workflow")
                        : Failure(
                            canonical,
                            WorkspaceModuleResolutionError.ResourceNotFound);
                case WorkspaceModuleAddressKind.PluginSettings:
                    var plugin = _plugins.Get(canonical.ResourceId);
                    if (plugin is null)
                    {
                        return Failure(
                            canonical,
                            WorkspaceModuleResolutionError.ResourceNotFound);
                    }
                    if (plugin.Instance is null)
                        await plugin.EnsureActivatedAsync();
                    if (plugin.Instance is not IHasSettingsUI)
                    {
                        return Failure(
                            canonical,
                            WorkspaceModuleResolutionError.ResourceUnsupported);
                    }
                    return Success(
                        canonical,
                        $"{plugin.DisplayName} - {Text("plugins.settings", "设置")}",
                        searchScopeId: $"plugin:{plugin.Id}");
                default:
                    return Failure(
                        canonical,
                        WorkspaceModuleResolutionError.InvalidAddress);
            }
        }

        private WorkspaceModuleResolution Success(
            WorkspaceModuleAddress address,
            string title,
            bool canClose = true,
            string? searchScopeId = null)
            => new(
                address,
                new WorkspaceModuleDescriptor(
                    new WorkspaceModuleKey(
                        address.CanonicalValue[..address.CanonicalValue.IndexOf(':')],
                        address.ResourceId),
                    title,
                    canClose,
                    searchScopeId: searchScopeId),
                WorkspaceModuleResolutionError.None);

        private static WorkspaceModuleResolution Failure(
            WorkspaceModuleAddress address,
            WorkspaceModuleResolutionError error)
            => new(address, null, error);

        private string Text(string key, string fallback)
        {
            var value = _localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
