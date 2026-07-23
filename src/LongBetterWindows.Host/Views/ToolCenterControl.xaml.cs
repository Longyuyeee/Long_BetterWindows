using System.IO;
using System.Windows;
using System.Windows.Controls;
using LongBetterWindows.Host.Interaction;
using System.Windows.Input;
using System.Windows.Media;
using LongBetterWindows.Host.Engine;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Views
{
    public partial class ToolCenterControl : UserControl
    {
        internal event EventHandler? WorkflowReviewClosed;
        internal event Action<bool>? WorkflowLayoutChanged;
        internal event Action<WorkflowExecutionResultState>? WorkflowExecutionResultChanged;
        internal event EventHandler? WorkflowTerminalOutputsCleared;
        private bool _columnEnabled;
        private bool _contextMenuRegistered;
        private bool _startupEnabled;
        private bool _docsLoaded;
        private bool? _isNarrowLayout;

        public ToolCenterControl()
        {
            InitializeComponent();
            SizeChanged += (_, _) => ApplyResponsiveLayout(ActualWidth);
            RefreshColumnStatus();
            RefreshContextMenuStatus();
            RefreshStartupStatus();
            RefreshMouseGestureControls();
            ShowPage("overview");

            // 同步主题按钮状态
            var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            _isLightMode = currentTheme == Wpf.Ui.Appearance.ApplicationTheme.Light;

            // 首次运行显示欢迎横幅
            if (App.IsFirstRun())
                WelcomeBanner.Visibility = Visibility.Visible;

            // 动态更新关于信息
            UpdateAboutInfo();

            // 插件管理控件自行刷新列表；ToolCenter 只同步全局概览数据。
            HostProvider.Instance.PluginStore.PluginsChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateAboutInfo();
                });
            };
        }

        private void RefreshMouseGestureControls()
        {
            var mode = ServicesInitializer.MouseGestures.Mode;
            foreach (var button in MouseGestureButtons.Children.OfType<Button>())
            {
                var active = string.Equals(
                    button.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase);
                button.SetResourceReference(
                    FrameworkElement.StyleProperty,
                    active ? "LongButton.Primary" : "LongButton");
            }
            MouseGestureStatusText.Text = mode switch
            {
                MouseGestureMode.MiddleButton => "已启用：按下鼠标中键呼出",
                MouseGestureMode.LongRightPress => $"已启用：静止长按右键 {LongRightPressRecognizer.HoldMilliseconds}ms 呼出",
                _ => "当前关闭，不监听全局鼠标按键",
            };
        }

        private async void MouseGestureMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string value }
                || !Enum.TryParse<MouseGestureMode>(value, out var mode)) return;
            MouseGestureButtons.IsEnabled = false;
            try
            {
                await ServicesInitializer.MouseGestures.SetModeAsync(mode);
                RefreshMouseGestureControls();
            }
            finally { MouseGestureButtons.IsEnabled = true; }
        }

        private void ApplyResponsiveLayout(double width)
        {
            var isNarrow = width < 860;
            if (_isNarrowLayout == isNarrow) return;
            _isNarrowLayout = isNarrow;
            NavigationColumn.Width = new GridLength(isNarrow ? 160 : 220);
            PageHeader.Margin = isNarrow
                ? new Thickness(18, 16, 18, 12)
                : new Thickness(32, 20, 32, 16);
            ContentBodyFrame.Padding = isNarrow
                ? new Thickness(18, 0, 18, 18)
                : new Thickness(32, 0, 32, 32);

            if (isNarrow)
            {
                OverviewPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(0);
                OverviewStatusColumn.Width = new GridLength(0);
                Grid.SetRow(OverviewStatusCard, 1);
                Grid.SetColumn(OverviewStatusCard, 0);
                OverviewStatusCard.Margin = new Thickness(0, 12, 0, 0);
            }
            else
            {
                OverviewPrimaryColumn.Width = new GridLength(1.3, GridUnitType.Star);
                OverviewGapColumn.Width = new GridLength(16);
                OverviewStatusColumn.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetRow(OverviewStatusCard, 0);
                Grid.SetColumn(OverviewStatusCard, 2);
                OverviewStatusCard.Margin = new Thickness(0);
            }
        }

        private void UpdateAboutInfo()
        {
            var plugins = HostProvider.Instance.PluginStore.GetAll();
            var capCount = Engine.ManifestReader.KnownCapabilities.Count;
            AboutVersion.Text = $"v{App.ProductVersion} · .NET 8.0 · WPF";
            AboutStats.Text = $"{capCount} 项原子能力 · {plugins.Count} 个插件 · 3 种运行时";
            OverviewPluginCount.Text = plugins.Count.ToString();
            OverviewCommandCount.Text = HostProvider.Instance.PluginStore.Commands.Count.ToString();
            OverviewCapabilityCount.Text = capCount.ToString();
        }

        private async void ClearSearchPreferences_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            await ServicesInitializer.SearchPreferences.ClearAsync();
            await ServicesInitializer.SuperPanelGroups.ClearAsync();
            SearchPreferenceStatusText.Text = "固定、最近使用与自定义分组已清除";
            button.IsEnabled = true;
        }

        #region Navigation

        private string _activePage = "overview";

        internal void OpenMarketForQuality() => ShowPage("market");
        internal void OpenDiagnosticsForQuality() => ShowPage("diagnostics");
        internal void OpenPluginsForQuality() => ShowPage("plugins");

        internal async Task<string?> OpenWorkflowReviewAsync(
            string workflowId,
            string? expectedStateFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            ShowPage("workflows");
            if (WorkflowEditorHost.Content is not WorkflowEditorControl editor)
                return "组合动作编辑器当前不可用。";
            return await editor.OpenExecutionReviewAsync(
                workflowId,
                expectedStateFingerprint,
                cancellationToken);
        }

        internal bool CancelWorkflowReview()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.CancelExecutionReview();

        internal bool DuplicateCurrentWorkflow()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.DuplicateCurrentWorkflow();

        internal bool IsWorkflowLayoutCompact
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.IsCompactLayout;

        internal double WorkflowLayoutWidth
            => WorkflowEditorHost.ActualWidth;

        internal bool ClearWorkflowTerminalOutputs()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.ClearTerminalOutputs();

        internal WorkflowTerminalOutput? GetWorkflowTerminalOutputForQuality()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                ? editor.GetTerminalOutputForQuality()
                : null;

        internal bool ToggleWorkflowTerminalOutputApproval()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                && editor.ToggleTerminalOutputApproval();

        internal Task<bool> ConfirmWorkflowReviewAsync()
            => WorkflowEditorHost.Content is WorkflowEditorControl editor
                ? editor.ConfirmExecutionReviewAsync()
                : Task.FromResult(false);

        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string page })
                ShowPage(page);
        }

        private void ShowPage(string page)
        {
            (string Key, FrameworkElement Panel, RadioButton Navigation, string Title, string Subtitle)[] pages =
            {
                ("overview", PanelOverview, NavOverview, "概览", "平台状态、关键能力和下一步操作"),
                ("workflows", PanelWorkflows, NavWorkflows, "组合动作", "编排、预检并安全保存跨插件工作流"),
                ("plugins", PanelPlugins, NavPlugins, "插件", "管理已安装插件、运行状态、权限和设置"),
                ("market", PanelMarket, NavMarket, "插件市场", "发现、审查并安装可信的 Long 原生插件"),
                ("system", PanelSystem, NavSystem, "系统集成", "配置 Explorer、启动项和全局快捷键"),
                ("diagnostics", PanelDiagnostics, NavDiagnostics, "平台诊断", "查看宿主资源、插件调用和能力使用情况"),
                ("developer", PanelDev, NavDeveloper, "开发者", "创建、调试、打包插件并查阅开发资源"),
                ("settings", PanelSettings, NavSettings, "设置", "外观、动效、本地数据与隐私"),
            };

            foreach (var (key, panel, navigation, title, subtitle) in pages)
            {
                var selected = key == page;
                panel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                navigation.IsChecked = selected;
                if (!selected) continue;

                _activePage = key;
                PageTitle.Text = title;
                PageSubtitle.Text = subtitle;
                if (key == "plugins")
                    PluginManagementHost.Refresh();
                else if (key == "workflows" && WorkflowEditorHost.Content == null)
                {
                    var editor = new WorkflowEditorControl();
                    editor.ExecutionReviewClosed += (_, _) =>
                        WorkflowReviewClosed?.Invoke(this, EventArgs.Empty);
                    editor.ResponsiveLayoutChanged += compact =>
                        WorkflowLayoutChanged?.Invoke(compact);
                    editor.ExecutionResultChanged += result =>
                        WorkflowExecutionResultChanged?.Invoke(result);
                    editor.TerminalOutputsCleared += (_, _) =>
                        WorkflowTerminalOutputsCleared?.Invoke(this, EventArgs.Empty);
                    WorkflowEditorHost.Content = editor;
                }
                else if (key == "market" && MarketHost.Content == null)
                    MarketHost.Content = new MarketplaceControl();
                else if (key == "diagnostics" && DiagnosticsHost.Content == null)
                    DiagnosticsHost.Content = new PerformancePanel();
                else if (key == "developer" && !_docsLoaded)
                {
                    RefreshDocLinks();
                    _docsLoaded = true;
                }
                Helpers.AnimationHelper.FadeInElement(panel, durationMs: 160);
            }
        }

        private void WelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            WelcomeBanner.Visibility = Visibility.Collapsed;
            App.MarkOnboarded();
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                var pages = new[] { "overview", "workflows", "plugins", "market", "system", "diagnostics", "developer", "settings" };
                var current = Array.IndexOf(pages, _activePage);
                var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                ShowPage(pages[(current + direction + pages.Length) % pages.Length]);
            }
        }

        #endregion

        #region Shared Brush helpers (from Colors.xaml)

        private Brush GreenBrush => (Brush)FindResource("SuccessGreenBrush");
        private Brush GrayBrush => (Brush)FindResource("TextSecondaryBrush");
        private Brush BlueBrush => (Brush)FindResource("AccentBlueBrush");
        private Brush RedBrush => (Brush)FindResource("DangerRedBrush");

        #endregion

        private void DevTools_Click(object sender, RoutedEventArgs e)
        {
            PluginDevTools.Open(Window.GetWindow(this)!);
        }

        private void DesignSystemPreview_Click(object sender, RoutedEventArgs e)
        {
            var preview = new DesignSystemPreview
            {
                Owner = Window.GetWindow(this),
            };
            preview.Show();
        }

        private void OpenPalette_Click(object sender, RoutedEventArgs e)
            => CommandPaletteWindow.ShowPalette();

        private static bool _isLightMode;
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isLightMode = !_isLightMode;

            // 使用 WPF-UI 主题管理器切换整个 Application
            var theme = _isLightMode
                ? Wpf.Ui.Appearance.ApplicationTheme.Light
                : Wpf.Ui.Appearance.ApplicationTheme.Dark;
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme);
            App.SaveThemeSetting(_isLightMode);
            App.UpdateThemeResources(_isLightMode);

            if (sender is Button btn)
                btn.Content = _isLightMode ? "暗色" : "亮色";
        }

        private void RefreshDocLinks()
        {
            DocLinksPanel.Children.Clear();

            var docsDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "docs"));

            if (!Directory.Exists(docsDir))
                docsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");

            if (!Directory.Exists(docsDir))
            {
                DocLinksPanel.Children.Add(new TextBlock
                {
                    Text = "文档目录未找到",
                    FontSize = 11,
                    Foreground = GrayBrush,
                });
                return;
            }

            var docFiles = Directory.GetFiles(docsDir, "*.md")
                .OrderBy(f => f)
                .ToList();

            foreach (var file in docFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file)
                    .Replace("设计文档_", "")
                    .Replace("第一部分_", "Part1 ")
                    .Replace("第二部分_", "Part2 ")
                    .Replace("第四部分_", "Part4 ")
                    .Replace("插件开发指南", "插件开发指南 ⭐")
                    .Replace("基础能力_API_手册", "API 手册");

                var link = new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = BlueBrush,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = file,
                };
                link.MouseLeftButtonDown += (_, _) =>
                {
                    var path = (string)link.Tag;
                    var content = File.ReadAllText(path);
                    DocViewer.ShowDoc(
                        Window.GetWindow(this)!,
                        Path.GetFileNameWithoutExtension(path),
                        content);
                };

                DocLinksPanel.Children.Add(link);
            }
        }

        private void StartupButton_Click(object sender, RoutedEventArgs e)
        {
            _startupEnabled = !_startupEnabled;
            ServicesInitializer.Startup.SetAutoStart(_startupEnabled);
            RefreshStartupStatus();
        }

        private void RefreshStartupStatus()
        {
            _startupEnabled = ServicesInitializer.Startup.IsAutoStartEnabled;
            StartupButton.Content = _startupEnabled ? "禁用" : "启用";

            if (_startupEnabled)
            {
                StartupStatusText.Text = "已启用";
                StartupStatusText.Foreground = GreenBrush;
            }
            else
            {
                StartupStatusText.Text = "未启用";
                StartupStatusText.Foreground = GrayBrush;
            }
        }

        private async void ColumnButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnButton.IsEnabled = false;
            ColumnStatusText.Text = "处理中...";

            try
            {
                if (_columnEnabled)
                {
                    var result = await ServicesInitializer.ColumnInjection
                        .DisableCommentColumnAsync();
                    if (result.IsSuccess)
                    {
                        _columnEnabled = false;
                        ColumnStatusText.Text = "备注列已移除";
                        ColumnStatusText.Foreground = GrayBrush;
                    }
                    else
                        ColumnStatusText.Text = "移除失败: " + (result.ErrorMessage ?? "未知错误");
                }
                else
                {
                    var result = await ServicesInitializer.ColumnInjection
                        .EnableCommentColumnAsync();
                    if (result.IsSuccess)
                    {
                        _columnEnabled = true;
                        ColumnStatusText.Text = "备注列已启用 · Explorer 已刷新";
                        ColumnStatusText.Foreground = GreenBrush;
                    }
                    else
                        ColumnStatusText.Text = "注入失败: " + (result.ErrorMessage ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                ColumnStatusText.Text = "操作异常: " + ex.Message;
                ColumnStatusText.Foreground = RedBrush;
            }

            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";
            ColumnButton.IsEnabled = true;
        }

        private async void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenuButton.IsEnabled = false;
            ContextMenuStatusText.Text = "处理中...";

            try
            {
                if (_contextMenuRegistered)
                {
                    var result = await ServicesInitializer.ContextMenu.UnregisterAsync();
                    if (result.IsSuccess)
                    {
                        _contextMenuRegistered = false;
                        ContextMenuStatusText.Text = "已移除";
                        ContextMenuStatusText.Foreground = GrayBrush;
                    }
                    else
                        ContextMenuStatusText.Text = "移除失败: " + (result.ErrorMessage ?? "未知错误");
                }
                else
                {
                    var result = await ServicesInitializer.ContextMenu.RegisterAsync();
                    if (result.IsSuccess)
                    {
                        _contextMenuRegistered = true;
                        ContextMenuStatusText.Text = "已注册 · 右键文件夹即可使用";
                        ContextMenuStatusText.Foreground = GreenBrush;
                    }
                    else
                        ContextMenuStatusText.Text = "注册失败: " + (result.ErrorMessage ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                ContextMenuStatusText.Text = "操作异常: " + ex.Message;
                ContextMenuStatusText.Foreground = RedBrush;
            }

            ContextMenuButton.Content = _contextMenuRegistered ? "移除" : "注册";
            ContextMenuButton.IsEnabled = true;
        }

        private void RefreshColumnStatus()
        {
            _columnEnabled = ServicesInitializer.ColumnInjection.IsCommentColumnEnabled;
            ColumnButton.Content = _columnEnabled ? "移除" : "一键开启";

            if (_columnEnabled)
            {
                ColumnStatusText.Text = "已启用";
                ColumnStatusText.Foreground = GreenBrush;
            }
            else
            {
                ColumnStatusText.Text = "未启用";
                ColumnStatusText.Foreground = GrayBrush;
            }
        }

        private void RefreshContextMenuStatus()
        {
            _contextMenuRegistered = ServicesInitializer.ContextMenu.IsRegistered;
            ContextMenuButton.Content = _contextMenuRegistered ? "移除" : "注册";

            if (_contextMenuRegistered)
            {
                ContextMenuStatusText.Text = "已注册";
                ContextMenuStatusText.Foreground = GreenBrush;
            }
            else
            {
                ContextMenuStatusText.Text = "未注册";
                ContextMenuStatusText.Foreground = GrayBrush;
            }
        }

    }
}
