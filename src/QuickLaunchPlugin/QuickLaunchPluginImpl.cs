using System.Windows;
using System.Security.Cryptography;
using System.Text;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Interaction;
using Serilog;

namespace QuickLaunchPlugin;

public class QuickLaunchPluginImpl :
    ILongPlugin,
    IHasMainUI,
    IPluginCommandHandler,
    ISearchProvider,
    IPluginLanguageLifecycle
{
    private IHostApi? _host;
    private bool _isActive;
    private LaunchWindow? _window;
    private readonly QuickLaunchTargetPolicy _targetPolicy = new();
    private IReadOnlyDictionary<string, string> _strings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string Id => "com.long.quicklaunch";
    public string Name => Text("plugin.name", "快捷启动器");
    public string Version => "1.1.1";
    public int Priority => 180;
    public PluginState State { get; private set; } = PluginState.Loaded;

    public Task<bool> InitializeAsync(IHostApi host)
    {
        _host = host;
        Log.Information("[QuickLaunch] 已接入统一命令入口");
        return Task.FromResult(true);
    }

    public Task<bool> StartAsync()
    {
        State = PluginState.Running;
        return Task.FromResult(true);
    }

    public Task<bool> StopAsync()
    {
        var application = Application.Current;
        if (application is not null)
            application.Dispatcher.Invoke(() => _window?.Close());
        _window = null;
        _isActive = false;
        State = PluginState.Stopped;
        return Task.FromResult(true);
    }

    public void ShowMainUI() => ShowLauncher();

    public async Task<PluginCommandResult> ExecuteCommandAsync(
        PluginCommandInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments.TryGetValue("action", out var action)
            && action == "open-result"
            && !string.IsNullOrWhiteSpace(invocation.Text))
        {
            var category = invocation.Arguments.TryGetValue("category", out var value)
                ? value
                : "application";
            return await ExecuteTargetAsync(
                category,
                invocation.Text,
                cancellationToken);
        }

        ShowLauncher(invocation.Text);
        return PluginCommandResult.Success();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawQuery = request.Query.Trim();
        var scopePrefix = new[]
            {
                Text("search.scopePrefix", "快捷启动："),
                "快捷启动：",
                "Quick Launch: ",
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(prefix => rawQuery.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));
        var scoped = scopePrefix is not null;
        var query = scoped
            ? rawQuery[scopePrefix!.Length..].Trim()
            : rawQuery;
        var preferredIds = (request.PinnedResultIds ?? Array.Empty<string>())
            .Concat(request.RecentResultIds ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var recallingPreferences = query.Length == 0 && preferredIds.Count > 0;
        if (!recallingPreferences && query.Length < (scoped ? 1 : 2))
            return Array.Empty<SearchResultItem>();

        var applications = await Task.Run(
            () => LaunchWindow.GetApplications(),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Min(scoped ? 10 : 5, request.MaxResults);
        List<SmartEntry> matches;
        if (recallingPreferences)
        {
            var order = preferredIds
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            matches = applications
                .Where(entry => order.ContainsKey(Id + ":" + BuildResultId(entry)))
                .OrderBy(entry => order[Id + ":" + BuildResultId(entry)])
                .Take(limit)
                .ToList();
        }
        else
        {
            matches = applications
                .Where(entry => entry.Name.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();
        }

        var results = matches.Select((entry, index) => new SearchResultItem
        {
            Id = BuildResultId(entry),
            ProviderId = Id,
            Title = entry.Name,
            Subtitle = recallingPreferences
                ? Text(
                    "search.recallingSubtitle",
                    "固定或最近使用的开始菜单应用")
                : scoped
                ? Text(
                    "search.scopedSubtitle",
                    "来自系统开始菜单 · Enter 立即打开")
                : Text("search.defaultSubtitle", "开始菜单应用"),
            Source = Text(
                "search.applicationSource",
                "快捷启动器 · 应用"),
            Score = (recallingPreferences ? 100 : scoped ? 900 : 720) - index,
            Kind = SearchResultKind.Data,
            PrimaryAction = new SearchResultAction(
                SearchActionKind.ExecuteCommand,
                Id + ":launcher.open",
                new PluginCommandInvocation
                {
                    CommandId = "launcher.open",
                    InputType = AcceptedInputType.Text,
                    Text = entry.Path,
                    Arguments = new Dictionary<string, string>
                    {
                        ["action"] = "open-result",
                        ["category"] = entry.Category,
                    },
                }),
            CanPin = true,
        }).ToList();

        if (!recallingPreferences && !scoped && matches.Count > 0
            && results.Count < request.MaxResults)
        {
            results.Add(new SearchResultItem
            {
                Id = "continue:" + query,
                ProviderId = Id,
                Title = string.Format(
                    Text(
                        "search.continueTitle",
                        "在快捷启动器中继续搜索“{0}”"),
                    query),
                Subtitle = Text(
                    "search.continueSubtitle",
                    "进入插件数据源，显示更多开始菜单结果"),
                Source = Text("search.source", "快捷启动器"),
                Score = 520,
                Kind = SearchResultKind.Continuation,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.ContinueSearch,
                    Text("search.scopePrefix", "快捷启动：") + query),
                ContinuationToken = query,
            });
        }

        return results;
    }

    private static string BuildResultId(SmartEntry entry)
    {
        var value = entry.Path ?? entry.Name;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return "app:" + Convert.ToHexString(hash[..12]);
    }

    private void ShowLauncher(string? initialQuery = null)
    {
        if (_isActive) return;
        _isActive = true;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = LaunchWindow.Show(
                OnEntrySelected,
                CreateWindowLocalization(),
                initialQuery);
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
                _isActive = false;
            };
        });
    }

    private async void OnEntrySelected(SmartEntry? entry)
    {
        _isActive = false;
        if (entry?.Path is not { Length: > 0 } path || _host == null) return;

        try
        {
            var result = await ExecuteTargetAsync(
                entry.Category,
                path,
                CancellationToken.None);
            if (!result.IsSuccess)
                Log.Warning(
                    "[QuickLaunch] 目标执行被拒绝或失败: {Category}, {Error}",
                    entry.Category,
                    result.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuickLaunch] 操作失败: {Path}", path);
        }
    }

    private async Task<PluginCommandResult> ExecuteTargetAsync(
        string category,
        string target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_host is null)
            return PluginCommandResult.Failure(Text(
                "error.notInitialized",
                "快捷启动器尚未初始化。"));

        var validation = _targetPolicy.Validate(category, target);
        if (!validation.IsValid)
        {
            return PluginCommandResult.Failure(string.Format(
                Text(
                    "error.targetRejected",
                    "目标已被安全策略拒绝：{0}"),
                validation.Error));
        }

        var response = category == "calculation"
            ? await _host.Clipboard.SetTextAsync(validation.NormalizedTarget!)
            : await _host.ShellExecute.OpenWithDefaultAsync(
                validation.NormalizedTarget!);
        if (!response.IsSuccess)
        {
            return PluginCommandResult.Failure(
                response.ErrorMessage
                ?? Text("error.openFailed", "无法打开目标。"));
        }

        Log.Information(
            "[QuickLaunch] 已执行动态搜索结果: {Category}",
            category);
        return PluginCommandResult.Success();
    }

    public Task OnLanguageChangedAsync(
        PluginLanguageContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _strings = context.Resources;
        var application = Application.Current;
        if (application is not null)
        {
            application.Dispatcher.Invoke(() =>
                _window?.ApplyLocalization(CreateWindowLocalization()));
        }
        return Task.CompletedTask;
    }

    private LaunchWindowLocalization CreateWindowLocalization()
        => new(
            Text("window.title", "快捷启动"),
            Text("window.searchAutomationName", "搜索应用、文件、链接或计算"),
            Text("window.emptyHint", "输入应用、文件、链接或算式"),
            Text("window.resultCount", "{0} 个结果"),
            Text("window.noMatches", "无匹配结果"),
            Text("window.navigationHint", "↑↓ 选择 · Enter 打开"),
            Text("window.openLink", "打开 {0}"),
            Text("category.application", "应用"),
            Text("category.calculation", "计算"),
            Text("category.link", "链接"),
            Text("category.file", "文件"),
            Text("category.content", "内容匹配"),
            Text("subtitle.application", "开始菜单应用"),
            Text("subtitle.calculation", "Enter 复制到剪贴板"),
            Text("subtitle.link", "Enter 浏览器打开"));

    private string Text(string key, string fallback)
        => _strings.TryGetValue(key, out var value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
}
