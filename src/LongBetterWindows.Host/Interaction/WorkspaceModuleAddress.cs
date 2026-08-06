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
        Widgets,
        PluginSettings,
        PluginRuntime,
    }

    internal readonly record struct WorkspaceModuleAddress(
        WorkspaceModuleAddressKind Kind,
        string ResourceId,
        string? InstanceId = null)
    {
        public string CanonicalValue => InstanceId is null
            ? $"{GetKindName(Kind)}:{ResourceId}"
            : $"{GetKindName(Kind)}:{ResourceId}:{InstanceId}";

        public static bool TryParse(
            string? value,
            out WorkspaceModuleAddress address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var candidate = value.Trim();
            var parts = candidate.Split(':');
            if (parts.Length is < 2 or > 3
                || parts.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            var kindText = parts[0];
            var resourceId = parts[1];
            if (!TryParseKind(kindText, out var kind)
                || !IsAllowedResource(kind, resourceId)
                || (kind == WorkspaceModuleAddressKind.PluginRuntime)
                    != (parts.Length == 3))
            {
                return false;
            }

            if (kind is not WorkspaceModuleAddressKind.Workflow
                and not WorkspaceModuleAddressKind.PluginSettings
                and not WorkspaceModuleAddressKind.PluginRuntime)
            {
                resourceId = resourceId.ToLowerInvariant();
            }
            var instanceId = parts.Length == 3 ? parts[2] : null;
            if (instanceId is not null && !IsSafeIdentifier(instanceId))
                return false;
            address = new WorkspaceModuleAddress(
                kind,
                resourceId,
                instanceId?.ToLowerInvariant());
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
                "widgets" => WorkspaceModuleAddressKind.Widgets,
                "plugin-settings" => WorkspaceModuleAddressKind.PluginSettings,
                "plugin-runtime" => WorkspaceModuleAddressKind.PluginRuntime,
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
                WorkspaceModuleAddressKind.Settings => null,
                WorkspaceModuleAddressKind.Diagnostics => "root",
                WorkspaceModuleAddressKind.Developer => "root",
                WorkspaceModuleAddressKind.Widgets => "root",
                _ => null,
            };
            if (fixedResource is not null)
            {
                return fixedResource.Equals(
                    resourceId,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (kind == WorkspaceModuleAddressKind.Settings)
            {
                return resourceId.Equals("root", StringComparison.OrdinalIgnoreCase)
                    || resourceId.Equals("appearance", StringComparison.OrdinalIgnoreCase)
                    || resourceId.Equals("interaction", StringComparison.OrdinalIgnoreCase)
                    || resourceId.Equals("connections", StringComparison.OrdinalIgnoreCase)
                    || resourceId.Equals("updates", StringComparison.OrdinalIgnoreCase);
            }

            return IsSafeIdentifier(resourceId);
        }

        private static bool IsSafeIdentifier(string value)
            => value.Length <= 128
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

        private static string GetKindName(WorkspaceModuleAddressKind kind)
            => kind switch
            {
                WorkspaceModuleAddressKind.Management => "management",
                WorkspaceModuleAddressKind.Marketplace => "marketplace",
                WorkspaceModuleAddressKind.Settings => "settings",
                WorkspaceModuleAddressKind.Diagnostics => "diagnostics",
                WorkspaceModuleAddressKind.Developer => "developer",
                WorkspaceModuleAddressKind.Workflow => "workflow",
                WorkspaceModuleAddressKind.Widgets => "widgets",
                WorkspaceModuleAddressKind.PluginSettings => "plugin-settings",
                WorkspaceModuleAddressKind.PluginRuntime => "plugin-runtime",
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
        private readonly PluginWorkspaceSessionManager _pluginSessions;

        public WorkspaceModuleResolver(
            PluginRegistry plugins,
            CommandWorkflowRepository workflows,
            Func<string, string>? localize = null,
            PluginWorkspaceSessionManager? pluginSessions = null)
        {
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
            _localize = localize;
            _pluginSessions = pluginSessions ?? new PluginWorkspaceSessionManager();
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
                    return Success(
                        canonical,
                        Text("page.settings.title", "设置"),
                        moduleResourceId: "root",
                        navigationTarget: canonical.ResourceId == "root"
                            ? null
                            : canonical.ResourceId);
                case WorkspaceModuleAddressKind.Diagnostics:
                    return Success(canonical, Text("page.diagnostics.title", "诊断"));
                case WorkspaceModuleAddressKind.Developer:
                    return Success(canonical, Text("page.developer.title", "开发者"));
                case WorkspaceModuleAddressKind.Widgets:
                    return Success(
                        canonical,
                        Text("page.widgets.title", "桌面组件"));
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
                    return Success(
                        canonical,
                        $"{plugin.DisplayName} - {Text("plugins.settings", "设置")}",
                        searchScopeId: $"plugin:{plugin.Id}");
                case WorkspaceModuleAddressKind.PluginRuntime:
                    var runtimePlugin = _plugins.Get(canonical.ResourceId);
                    var session = _pluginSessions.GetBySessionId(
                        canonical.InstanceId!);
                    if (runtimePlugin is null
                        || session is null
                        || session.State.IsEnded
                        || !string.Equals(
                            session.State.PluginId,
                            runtimePlugin.Id,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Failure(
                            canonical,
                            WorkspaceModuleResolutionError.ResourceNotFound);
                    }
                    return Success(
                        canonical,
                        runtimePlugin.DisplayName,
                        supportsDetach: true);
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
            bool supportsDetach = false,
            string? searchScopeId = null,
            string? moduleResourceId = null,
            string? navigationTarget = null)
            => new(
                address,
                new WorkspaceModuleDescriptor(
                    new WorkspaceModuleKey(
                        address.CanonicalValue[..address.CanonicalValue.IndexOf(':')],
                        moduleResourceId ?? address.ResourceId,
                        address.InstanceId),
                    title,
                    canClose,
                    supportsDetach,
                    searchScopeId: searchScopeId,
                    navigationTarget: navigationTarget),
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
