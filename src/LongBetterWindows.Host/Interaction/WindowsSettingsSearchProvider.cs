namespace LongBetterWindows.Host.Interaction
{
    public sealed class WindowsSettingsSearchProvider : ISearchProvider
    {
        private static readonly IReadOnlyList<SettingEntry> Entries = new[]
        {
            Entry("显示", "调整缩放、亮度和多显示器", "ms-settings:display", "屏幕", "分辨率", "display", "monitor"),
            Entry("声音", "管理输出、输入和音量", "ms-settings:sound", "音频", "麦克风", "sound", "audio"),
            Entry("通知", "配置应用通知和免打扰", "ms-settings:notifications", "消息", "notification"),
            Entry("电源和电池", "电源模式、睡眠和电池使用情况", "ms-settings:powersleep", "睡眠", "电池", "power", "battery"),
            Entry("存储", "查看空间占用和存储感知", "ms-settings:storagesense", "磁盘", "空间", "storage"),
            Entry("多任务处理", "贴靠窗口、桌面和 Alt+Tab", "ms-settings:multitasking", "分屏", "窗口", "multitasking"),
            Entry("剪贴板", "剪贴板历史记录和跨设备同步", "ms-settings:clipboard", "复制", "clipboard"),
            Entry("系统信息", "设备规格、Windows 版本和产品密钥", "ms-settings:about", "关于", "版本", "about"),
            Entry("蓝牙和设备", "连接蓝牙、键盘和其他设备", "ms-settings:bluetooth", "设备", "bluetooth"),
            Entry("打印机和扫描仪", "添加和管理打印设备", "ms-settings:printers", "打印", "扫描", "printer"),
            Entry("鼠标", "主按钮、指针速度和滚轮", "ms-settings:mousetouchpad", "指针", "滚轮", "mouse"),
            Entry("触摸板", "手势、点击和滚动设置", "ms-settings:devices-touchpad", "手势", "touchpad"),
            Entry("输入", "拼写、文本建议和触摸键盘", "ms-settings:typing", "键盘", "typing"),
            Entry("个性化", "背景、颜色、主题和锁屏", "ms-settings:personalization", "壁纸", "主题", "personalization"),
            Entry("已安装的应用", "卸载、移动和管理应用", "ms-settings:appsfeatures", "程序", "卸载", "apps"),
            Entry("默认应用", "按文件类型设置默认程序", "ms-settings:defaultapps", "打开方式", "default apps"),
            Entry("启动应用", "管理登录时自动运行的应用", "ms-settings:startupapps", "开机启动", "自启", "startup"),
            Entry("账户", "账户信息、家庭和其他用户", "ms-settings:accounts", "用户", "account"),
            Entry("登录选项", "Windows Hello、密码和动态锁", "ms-settings:signinoptions", "密码", "指纹", "人脸", "login"),
            Entry("网络和 Internet", "网络状态、流量和高级设置", "ms-settings:network", "网络", "internet"),
            Entry("Wi-Fi", "无线网络和已知网络", "ms-settings:network-wifi", "无线", "wifi"),
            Entry("以太网", "有线网络和 IP 设置", "ms-settings:network-ethernet", "网线", "ethernet"),
            Entry("VPN", "添加和管理 VPN 连接", "ms-settings:network-vpn", "代理", "vpn"),
            Entry("代理", "自动或手动代理服务器", "ms-settings:network-proxy", "proxy"),
            Entry("日期和时间", "时区、自动时间和格式", "ms-settings:dateandtime", "时间", "时区", "date", "time"),
            Entry("语言和区域", "显示语言、区域格式和输入法", "ms-settings:regionlanguage", "语言", "区域", "输入法", "language"),
            Entry("Windows 更新", "检查更新、暂停和更新历史", "ms-settings:windowsupdate", "系统更新", "update"),
            Entry("备份", "Windows 备份和 OneDrive 文件夹同步", "ms-settings:backup", "恢复", "backup"),
            Entry("隐私和安全性", "应用权限、诊断和 Windows 安全中心", "ms-settings:privacy", "隐私", "安全", "privacy"),
            Entry("位置", "管理设备和应用的位置权限", "ms-settings:privacy-location", "定位", "location"),
            Entry("相机权限", "允许应用访问相机", "ms-settings:privacy-webcam", "摄像头", "camera"),
            Entry("麦克风权限", "允许应用访问麦克风", "ms-settings:privacy-microphone", "录音", "microphone"),
            Entry("辅助功能", "视觉、听觉和交互辅助设置", "ms-settings:easeofaccess", "无障碍", "高对比度", "accessibility"),
            Entry("游戏栏", "录制游戏剪辑和快捷键", "ms-settings:gaming-gamebar", "录屏", "game bar"),
        };

        public string Id => "windows-settings";
        public int Priority => 520;

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = request.Query.Trim();
            if (query.Length == 0)
                return Task.FromResult<IReadOnlyList<SearchResultItem>>(
                    Array.Empty<SearchResultItem>());

            var results = Entries
                .Select(entry => (entry, score: Score(entry, query)))
                .Where(match => match.score > 0)
                .OrderByDescending(match => match.score)
                .ThenBy(match => match.entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Min(6, request.MaxResults))
                .Select(match => CreateResult(match.entry, match.score))
                .ToList();
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(results);
        }

        private static int Score(SettingEntry entry, string query)
        {
            if (entry.Title.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1000;
            if (entry.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 920;
            if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 840;
            if (entry.Keywords.Any(keyword => keyword.Equals(
                    query, StringComparison.OrdinalIgnoreCase))) return 780;
            if (entry.Keywords.Any(keyword => keyword.Contains(
                    query, StringComparison.OrdinalIgnoreCase))) return 700;
            return entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 620 : 0;
        }

        private SearchResultItem CreateResult(SettingEntry entry, int score)
            => new()
            {
                Id = "setting:" + entry.Uri["ms-settings:".Length..],
                ProviderId = Id,
                Title = entry.Title,
                Subtitle = entry.Description,
                Source = "Windows 设置",
                IconKind = SearchResultIconKind.Settings,
                Score = score,
                Kind = SearchResultKind.Data,
                PrimaryAction = new SearchResultAction(
                    SearchActionKind.OpenUri, entry.Uri, Label: "打开设置"),
                SecondaryActions = new[]
                {
                    new SearchResultAction(
                        SearchActionKind.CopyText, entry.Uri, Label: "复制设置 URI"),
                },
                CanPin = true,
            };

        private static SettingEntry Entry(
            string title,
            string description,
            string uri,
            params string[] keywords)
            => new(title, description, uri, keywords);

        private sealed record SettingEntry(
            string Title,
            string Description,
            string Uri,
            IReadOnlyList<string> Keywords);
    }
}
