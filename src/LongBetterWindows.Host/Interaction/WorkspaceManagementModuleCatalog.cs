namespace LongBetterWindows.Host.Interaction
{
    internal enum WorkspaceManagementPage
    {
        Overview,
        Workflows,
        Plugins,
        Widgets,
        Market,
        System,
        Diagnostics,
        Developer,
        Settings,
    }

    internal static class WorkspaceManagementModuleCatalog
    {
        public static WorkspaceModuleDescriptor Create(
            WorkspaceManagementPage page,
            Func<string, string>? localize = null)
            => page switch
            {
                WorkspaceManagementPage.Overview => Create(
                    "management",
                    "root",
                    Text(localize, "page.overview.title", "管理中心"),
                    canClose: false),
                WorkspaceManagementPage.Workflows => Create(
                    "management-page",
                    "workflows",
                    Text(localize, "page.workflows.title", "工作流"),
                    searchScopeId: "workflow"),
                WorkspaceManagementPage.Plugins => Create(
                    "management-page",
                    "plugins",
                    Text(localize, "page.plugins.title", "插件管理"),
                    searchScopeId: "plugins"),
                WorkspaceManagementPage.Widgets => Create(
                    "widgets",
                    "root",
                    Text(localize, "page.widgets.title", "桌面组件")),
                WorkspaceManagementPage.Market => Create(
                    "marketplace",
                    "catalog",
                    Text(localize, "page.market.title", "插件应用市场"),
                    searchScopeId: "marketplace"),
                WorkspaceManagementPage.System => Create(
                    "management-page",
                    "system",
                    Text(localize, "page.system.title", "系统集成")),
                WorkspaceManagementPage.Diagnostics => Create(
                    "diagnostics",
                    "root",
                    Text(localize, "page.diagnostics.title", "诊断")),
                WorkspaceManagementPage.Developer => Create(
                    "developer",
                    "root",
                    Text(localize, "page.developer.title", "开发者")),
                WorkspaceManagementPage.Settings => Create(
                    "settings",
                    "root",
                    Text(localize, "page.settings.title", "设置")),
                _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
            };

        public static bool TryResolvePage(
            WorkspaceModuleKey key,
            out WorkspaceManagementPage page)
        {
            var resolved = (key.Kind, key.ResourceId) switch
            {
                ("management", "root") => WorkspaceManagementPage.Overview,
                ("management-page", "workflows") => WorkspaceManagementPage.Workflows,
                ("management-page", "plugins") => WorkspaceManagementPage.Plugins,
                ("widgets", "root") => WorkspaceManagementPage.Widgets,
                ("marketplace", "catalog") => WorkspaceManagementPage.Market,
                ("management-page", "system") => WorkspaceManagementPage.System,
                ("diagnostics", "root") => WorkspaceManagementPage.Diagnostics,
                ("developer", "root") => WorkspaceManagementPage.Developer,
                ("settings", "root") => WorkspaceManagementPage.Settings,
                _ => (WorkspaceManagementPage?)null,
            };
            page = resolved.GetValueOrDefault();
            return resolved.HasValue;
        }

        private static WorkspaceModuleDescriptor Create(
            string kind,
            string resourceId,
            string title,
            bool canClose = true,
            string? searchScopeId = null)
            => new(
                new WorkspaceModuleKey(kind, resourceId),
                title,
                canClose,
                searchScopeId: searchScopeId);

        private static string Text(
            Func<string, string>? localize,
            string key,
            string fallback)
        {
            var value = localize?.Invoke(key);
            return string.IsNullOrWhiteSpace(value) || value == key
                ? fallback
                : value;
        }
    }
}
