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
    ISearchProvider
{
    private const string SearchScopePrefix = "快捷启动：";
    private IHostApi? _host;
    private bool _isActive;

    public string Id => "com.long.quicklaunch";
    public string Name => "快捷启动器";
    public string Version => "1.1.0";
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
            if (_host is null)
                return PluginCommandResult.Failure("快捷启动器尚未初始化。");

            var category = invocation.Arguments.TryGetValue("category", out var value)
                ? value
                : "应用";
            if (category == "计算")
                await _host.Clipboard.SetTextAsync(invocation.Text);
            else
                await _host.ShellExecute.OpenWithDefaultAsync(invocation.Text);

            Log.Information("[QuickLaunch] 已执行动态搜索结果: {Category}", category);
            return PluginCommandResult.Success();
        }

        ShowLauncher(invocation.Text);
        return PluginCommandResult.Success();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawQuery = request.Query.Trim();
        var scoped = rawQuery.StartsWith(
            SearchScopePrefix,
            StringComparison.OrdinalIgnoreCase);
        var query = scoped
            ? rawQuery[SearchScopePrefix.Length..].Trim()
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
                ? "固定或最近使用的开始菜单应用"
                : scoped
                ? "来自系统开始菜单 · Enter 立即打开"
                : "开始菜单应用",
            Source = "快捷启动器 · 应用",
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
                Title = $"在快捷启动器中继续搜索“{query}”",
                Subtitle = "进入插件数据源，显示更多开始菜单结果",
                Source = "快捷启动器",
                Score = 520,
                Kind = SearchResultKind.Continuation,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.ContinueSearch,
                    SearchScopePrefix + query),
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
            LaunchWindow.Show(OnEntrySelected, initialQuery));
    }

    private async void OnEntrySelected(SmartEntry? entry)
    {
        _isActive = false;
        if (entry?.Path is not { Length: > 0 } path || _host == null) return;

        try
        {
            if (entry.Category == "计算")
                await _host.Clipboard.SetTextAsync(path);
            else
                await _host.ShellExecute.OpenWithDefaultAsync(path);

            Log.Information("[QuickLaunch] 已处理: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[QuickLaunch] 操作失败: {Path}", path);
        }
    }
}
