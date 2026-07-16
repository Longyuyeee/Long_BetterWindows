<div align="center">

# 🪟 Long窗口·全能助手

**Windows 效率增强平台 | 热插拔插件系统 | Apple 风格 UI**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![Version](https://img.shields.io/badge/version-1.6.1-blue)](https://github.com/Longyuyeee/Long_BetterWindows/releases)

*一个功能强大、易于扩展的 Windows 效率工具平台*

[📥 下载](https://github.com/Longyuyeee/Long_BetterWindows/releases) | [📖 文档](docs/) | [🔌 插件开发](docs/插件开发指南.md) | [⭐ Star 支持](https://github.com/Longyuyeee/Long_BetterWindows)

</div>

---

## ✨ 核心特性

<table>
<tr>
<td width="50%">

### 🎨 Apple 风格设计
- 暖色调配色方案
- 流畅动画与过渡
- 大圆角 + 模糊效果
- 精致的卡片布局

</td>
<td width="50%">

### 🔌 热插拔插件系统
- 三种运行时（DLL/WebView/C#脚本）
- 无需重启即可热重载
- 权限沙盒机制
- .lpak 打包分发

</td>
</tr>
<tr>
<td>

### ⚡ 20 项底层能力
- 系统监控与性能分析
- 文件系统高级操作
- 网络端口管理
- 剪贴板监听与历史
- 音频控制与定时任务

</td>
<td>

### 🚀 21 个实用插件
- 开发者工具箱
- 剪贴板历史管理
- 端口管理器
- 硬件监控面板
- 文件整理器

</td>
</tr>
</table>

---

## 📦 快速开始

### 方式一：直接使用（推荐新用户）

1. **下载最新版本**
   ```
   前往 Releases 页面下载 Long_BetterWindows_v1.6.1.zip
   ```

2. **解压并运行**
   ```
   解压到任意目录，双击 LongBetterWindows.Host.exe
   ```

3. **打开工具中心**
   ```
   按下 Ctrl + Space 快捷键
   ```

### 方式二：源码编译（开发者）

```bash
# 克隆仓库
git clone https://github.com/Longyuyeee/Long_BetterWindows.git
cd Long_BetterWindows

# 一键启动（自动编译）
双击 启动.bat

# 或使用命令行
dotnet build
dotnet run --project src/LongBetterWindows.Host
```

---

## 🎯 功能亮点

### 🛠️ 开发者工具箱
<sup>**DevToolkit** - 一站式编码转换</sup>

- **Base64** 编码/解码
- **Hash** 计算（MD5/SHA1/SHA256）
- **UUID** 生成（v4/短UUID/时间戳）
- **时间戳** ↔ 日期转换
- **进制转换**（DEC/HEX/OCT/BIN）
- **URL/Unicode** 编码解码

### 📋 剪贴板历史
<sup>**ClipboardHistory** - 永不丢失的复制内容</sup>

- 自动记录剪贴板历史（最多 500 条）
- 按关键词/时间/类型搜索
- 一键复制回剪贴板
- 热键 `Ctrl+Shift+V` 快速打开

### 🌐 端口管理器
<sup>**PortManager** - 端口冲突终结者</sup>

- 扫描所有 TCP/UDP 端口
- 识别占用进程（PID/名称/路径）
- 占用分析与查找
- 安全终止进程释放端口

### 📊 硬件监控
<sup>**HardwareMonitor** - 系统状态一目了然</sup>

- 实时监控 CPU/内存/磁盘
- 系统信息展示
- 进程资源占用 Top 10
- 自动刷新（2秒间隔）

### 📁 文件整理器
<sup>**FileOrganizer** - 文件管理神器</sup>

- 按类型/日期/大小自动分类
- 查找大文件（自定义阈值）
- 删除重复文件（MD5 对比）
- 批量重命名
- 全文搜索

### 🎵 音频控制
<sup>**AudioService** - 快捷音量管理</sup>

- 获取/设置系统音量
- 静音/取消静音
- 音量增减控制
- 音频设备切换

---

## 🧩 底层能力清单

<details>
<summary><b>展开查看完整能力列表（20项）</b></summary>

### 系统能力
- `system.hotkey` - 全局热键
- `system.clipboard` - 剪贴板读写
- `system.clipboard.monitor` - 剪贴板监听 🆕
- `system.notification` - Toast 通知
- `system.screenshot` - 截图
- `system.input` - 输入模拟
- `system.process` - 进程管理
- `system.performance` - 性能监控 🆕
- `system.registry.*` - 注册表操作
- `system.cache` - 缓存清理 🆕
- `system.schedule` - 定时任务 🆕
- `system.audio` - 音频控制 🆕

### Shell 能力
- `shell.selection` - Explorer 感知
- `shell.execute` - 打开 URL/文件

### 文件系统
- `fs.ads.access` - NTFS 备用数据流
- `file.ops` - 文件操作
- `filesystem.advanced` - 高级文件系统 🆕

### 网络能力
- `network.ports` - 端口管理 🆕
- `network.http` - HTTP 请求

### 其他能力
- `text.pinyin` - 拼音匹配 🆕
- `storage.local` - 本地存储
- `window.info` - 窗口信息
- `ui.window` - UI 能力

</details>

---

## 🔌 插件开发

### 快速创建一个插件

```json
// manifest.json
{
  "id": "com.example.myplugin",
  "version": "1.0.0",
  "name": "我的插件",
  "entry_point": "index.html",
  "runtime": "webview",
  "capabilities": [
    "system.clipboard",
    "storage.local"
  ]
}
```

```html
<!-- index.html -->
<!DOCTYPE html>
<html>
<head>
    <title>我的插件</title>
</head>
<body>
    <h1>Hello Long窗口！</h1>
    <button onclick="getClipboard()">读取剪贴板</button>
    
    <script>
        async function getClipboard() {
            const text = await long.clipboard.getText();
            alert('剪贴板内容: ' + text);
        }
    </script>
</body>
</html>
```

详细教程请查看 [📖 插件开发指南](docs/插件开发指南.md)

---

## 📂 项目结构

```
Long_BetterWindows/
├── src/
│   ├── LongBetterWindows.Host/          # 宿主程序
│   │   ├── Capabilities/                # 能力接口
│   │   ├── Services/                    # 能力实现
│   │   ├── Engine/                      # 插件引擎
│   │   └── UI/                          # 主窗口 UI
│   ├── DevToolkit/                      # 开发者工具箱插件
│   ├── ClipboardHistory/                # 剪贴板历史插件
│   ├── PortManager/                     # 端口管理器插件
│   ├── HardwareMonitor/                 # 硬件监控插件
│   ├── FileOrganizer/                   # 文件整理器插件
│   └── ...（更多插件）
├── docs/                                # 文档
│   ├── 插件开发指南.md
│   ├── 脚本使用说明.md
│   └── v1.6持续扩展总结.md
├── 启动.bat                             # 一键启动脚本
├── 开发模式.bat                         # 开发模式脚本
├── 打包发布.bat                         # 打包发布脚本
└── README.md
```

---

## 📊 版本历程

| 版本 | 日期 | 更新内容 |
|------|------|----------|
| v1.6.1 | 2026-07-16 | 新增自动化脚本、优化文档 |
| v1.6.0 | 2026-07-16 | 新增 8 项底层能力、5 个插件 |
| v0.2.0 | 2026-07-15 | 插件热重载、权限沙盒、.lpak 打包 |
| v0.1.0 | 2026-07-14 | 项目初始化、基础架构 |

---

## 🛠️ 技术栈

- **框架**: .NET 8.0 + WPF
- **UI**: XAML + MaterialDesign
- **插件**: WebView2 + C# 脚本
- **日志**: Serilog
- **测试**: xUnit

---

## 🤝 贡献指南

我们欢迎任何形式的贡献！

1. **Fork 本仓库**
2. **创建特性分支** (`git checkout -b feature/AmazingFeature`)
3. **提交更改** (`git commit -m 'Add some AmazingFeature'`)
4. **推送到分支** (`git push origin feature/AmazingFeature`)
5. **提交 Pull Request**

---

## 📄 开源协议

本项目采用 [MIT](LICENSE) 协议开源。

---

## 💡 致谢

- 感谢 [Material Design In XAML](http://materialdesigninxaml.net/) 提供的优秀 UI 组件
- 感谢 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) 提供的网页渲染引擎
- 感谢 [Serilog](https://serilog.net/) 提供的日志框架

---

## 📮 联系方式

- **Issue**: [提交问题](https://github.com/Longyuyeee/Long_BetterWindows/issues)
- **Discussion**: [参与讨论](https://github.com/Longyuyeee/Long_BetterWindows/discussions)
- **Email**: longyuyeee@example.com

---

<div align="center">

**如果这个项目对你有帮助，请给一个 ⭐ Star 支持一下！**

Made with ❤️ by Kiro (Claude Code)

</div>
