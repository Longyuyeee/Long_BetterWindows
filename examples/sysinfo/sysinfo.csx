// 💻 系统信息面板 - 实时监控系统状态
//
// 功能：Ctrl+I 打开系统信息窗口
// 能力：sensor, window, process, hotkey
// 作者：Long_BetterWindows 示例

using System;
using System.Diagnostics;
using System.Text;

// 创建系统信息 HTML
string CreateSysInfoHtml()
{
    var html = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {
            font-family: 'Consolas', 'Microsoft YaHei', monospace;
            margin: 0;
            padding: 20px;
            background: #0f172a;
            color: #f8fafc;
        }
        h2 {
            margin: 0 0 20px 0;
            color: #38bdf8;
        }
        .info-grid {
            display: grid;
            grid-template-columns: repeat(2, 1fr);
            gap: 15px;
        }
        .info-card {
            background: #1e293b;
            padding: 15px;
            border-radius: 8px;
            border: 1px solid #334155;
        }
        .info-label {
            font-size: 12px;
            color: #94a3b8;
            margin-bottom: 5px;
        }
        .info-value {
            font-size: 24px;
            font-weight: bold;
            color: #38bdf8;
        }
        .info-unit {
            font-size: 14px;
            color: #94a3b8;
        }
        .process-list {
            margin-top: 20px;
            background: #1e293b;
            border-radius: 8px;
            padding: 15px;
        }
        .process-item {
            display: flex;
            justify-content: space-between;
            padding: 5px 0;
            border-bottom: 1px solid #334155;
        }
    </style>
</head>
<body>
    <h2>💻 系统信息</h2>
    <div class='info-grid'>
        <div class='info-card'>
            <div class='info-label'>CPU 温度</div>
            <div class='info-value' id='cpuTemp'>--</div>
            <div class='info-unit'>°C</div>
        </div>
        <div class='info-card'>
            <div class='info-label'>GPU 温度</div>
            <div class='info-value' id='gpuTemp'>--</div>
            <div class='info-unit'>°C</div>
        </div>
        <div class='info-card'>
            <div class='info-label'>内存使用</div>
            <div class='info-value' id='memory'>--</div>
            <div class='info-unit'>MB</div>
        </div>
        <div class='info-card'>
            <div class='info-label'>进程数</div>
            <div class='info-value' id='processCount'>--</div>
            <div class='info-unit'>个</div>
        </div>
    </div>

    <div class='process-list'>
        <h3>🔝 资源占用 TOP 5</h3>
        <div id='processList'></div>
    </div>

    <script>
        async function updateInfo() {
            // 模拟数据更新（实际应通过 API 获取）
            document.getElementById('cpuTemp').textContent = '45';
            document.getElementById('gpuTemp').textContent = '52';
            document.getElementById('memory').textContent = '8192';
            document.getElementById('processCount').textContent = '156';
        }

        // 每秒更新
        setInterval(updateInfo, 1000);
        updateInfo();
    </script>
</body>
</html>";
    return html;
}

// 注册热键
await Host.HotKey.RegisterAsync("Ctrl+I", async () =>
{
    var html = CreateSysInfoHtml();

    var windowId = await Host.UI.CreateWindowAsync(
        "系统信息",
        html,
        700,
        600,
        true
    );

    await Host.Notification.ShowAsync("💻 系统信息窗口已打开", "info");
});

Console.WriteLine("✅ 系统信息面板已加载 - 按 Ctrl+I 查看系统信息");
