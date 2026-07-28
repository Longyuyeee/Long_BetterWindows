namespace LongBetterWindows.Host.Interaction
{
    internal static class WorkspaceLegacyModuleCatalog
    {
        public static bool TryCreate(
            string page,
            Func<string, string>? localize,
            out WorkspaceModuleDescriptor? module)
        {
            module = page switch
            {
                "overview" => Create(
                    "management",
                    "root",
                    Text(localize, "page.overview.title", "管理中心"),
                    canClose: false),
                "workflows" => Create(
                    "management-page",
                    "workflows",
                    Text(localize, "page.workflows.title", "工作流"),
                    searchScopeId: "workflow"),
                "plugins" => Create(
                    "management-page",
                    "plugins",
                    Text(localize, "page.plugins.title", "插件管理"),
                    searchScopeId: "plugins"),
                "market" => Create(
                    "marketplace",
                    "catalog",
                    Text(localize, "page.market.title", "插件应用市场"),
                    searchScopeId: "marketplace"),
                "system" => Create(
                    "management-page",
                    "system",
                    Text(localize, "page.system.title", "系统集成")),
                "diagnostics" => Create(
                    "diagnostics",
                    "root",
                    Text(localize, "page.diagnostics.title", "诊断")),
                "developer" => Create(
                    "developer",
                    "root",
                    Text(localize, "page.developer.title", "开发者")),
                "settings" => Create(
                    "settings",
                    "root",
                    Text(localize, "page.settings.title", "设置")),
                _ => null,
            };
            return module is not null;
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
