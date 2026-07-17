<div align="center">

# 🪟 Long窗口·全能助手

**Windows 效率增强平台 | 热插拔插件系统 | Apple 风格 UI**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![Version](https://img.shields.io/badge/version-1.8.0-blue)](https://github.com/Longyuyeee/Long_BetterWindows/releases)

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

### ⚡ 25 项底层能力
- 系统监控与性能分析
- 文件系统高级操作
- 网络端口与流量监控
- 剪贴板监听与历史
- 壁纸、亮度、主题管理

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

## 🎯 功能亮点

### 🛠️ 系统管理
- **电源管理** - 关机/重启/睡眠/休眠/锁屏/电源状态
- **主题管理** - 暗色/亮色模式切换、系统强调色
- **壁纸管理** - 设置壁纸、多种样式（填充/适应/拉伸）
- **亮度控制** - 屏幕亮度调节（0-100）
- **音频控制** - 音量调节、静音切换

### 📊 性能监控
- **硬件监控** - CPU/GPU/内存/磁盘实时监控
- **进程管理** - 资源占用、进程关闭
- **网络监控** - 实时网速、流量统计
- **端口管理** - 端口扫描、进程映射、安全释放

### 📁 文件工具
- **文件整理** - 按类型/日期分类、查重、批量重命名
- **文件夹备注** - NTFS ADS 备注存储
- **剪贴板历史** - 自动记录、搜索、复用

### 💻 开发工具
- **开发者工具箱** - Base64/Hash/UUID/时间戳/进制/URL/Unicode
- **JSON 格式化** - 格式化、压缩、校验
- **文本对比** - 逐行对比、高亮差异
- **颜色选择器** - 取色、格式转换

### 🚀 效率助手
- **快捷启动** - 热键呼出、模糊搜索
- **宏录制器** - 录制回放鼠标键盘操作
- **窗口管理** - 快速平铺、多显示器支持
- **截图工具** - 区域截图、自动保存

---

## 📦 快速开始

### 方式一：直接使用（推荐新用户）

1. **下载最新版本**
   ```
   前往 Releases 页面下载 Long_BetterWindows_v1.8.0.zip
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

### 方式三：开发模式

```bash
# 支持热重载和详细日志
双击 开发模式.bat
```

---

## 🔌 插件系统

### 三种插件运行时

| 类型 | 技术栈 | 优势 | 适用场景 |
|---|---|---|---|
| **WebView** | HTML/CSS/JS | 快速开发、跨平台UI | 工具类插件、数据展示 |
| **DLL** | C# + WPF | 原生性能、系统集成 | 系统级功能、后台服务 |
| **脚本** | C# Script (.csx) | 无需编译、即写即用 | 快速原型、自动化脚本 |

### 内置插件列表（21个）

**系统工具**
- 📋 剪贴板历史 - 自动记录、快速搜索
- 🌐 端口管理器 - 端口扫描、进程识别
- 📊 硬件监控 - CPU/内存/磁盘监控
- 🗂️ 文件整理器 - 智能分类、批量处理
- 💻 开发者工具箱 - 编解码一站式工具

**效率工具**
- 🚀 快捷启动器 - 热键呼出、模糊搜索
- 📷 截图工具 - 快速截图、自动保存
- 🪟 窗口管理器 - 窗口平铺、多屏支持
- 📝 快速笔记 - 悬浮窗记事本
- 🔖 文件夹备注 - NTFS ADS 备注

**开发工具**
- 🧰 Base64 工具 - 编解码转换
- 📄 JSON 格式化 - 格式化、校验
- 🎨 颜色选择器 - 取色、格式转换
- 📝 Markdown 预览 - 实时渲染
- 🔄 文本对比 - 差异高亮

**其他工具**
- 🎯 宏录制器 - 自动化操作
- 📝 文件重命名 - 批量重命名
- 🔐 密码生成器 - 强密码生成
- 🌐 翻译助手 - 快速翻译
- 🔧 示例插件 - 开发参考

---

## 🎯 底层能力清单（25项）

### 系统能力（14项）
- `system.hotkey` - 全局热键
- `system.clipboard` - 剪贴板读写
- `system.clipboard.monitor` - 剪贴板监听
- `system.notification` - Toast 通知
- `system.screenshot` - 截图
- `system.input` - 输入模拟
- `system.process` - 进程管理
- `system.performance` - 性能监控
- `system.registry.*` - 注册表操作
- `system.cache` - 缓存清理
- `system.schedule` - 定时任务
- `system.audio` - 音频控制
- `system.power` - 电源管理
- `system.theme` - 主题管理
- `system.wallpaper` - 壁纸管理

### 显示能力（1项）
- `display.brightness` - 亮度控制

### 网络能力（3项）
- `network.ports` - 端口管理
- `network.monitor` - 流量监控
- `network.http` - HTTP 请求

### 文件能力（4项）
- `file.ops` - 文件操作
- `filesystem.advanced` - 高级文件系统
- `fs.ads.access` - NTFS ADS
- `shell.selection` - Explorer 感知
- `shell.execute` - 打开 URL/文件

### 其他能力（3项）
- `text.pinyin` - 拼音匹配
- `storage.local` - 本地存储
- `window.info` - 窗口信息
- `ui.window` - UI 窗口能力

---

## 📂 项目结构

```
Long_BetterWindows/
├── src/
│   ├── LongBetterWindows.Host/          # 宿主程序
│   │   ├── Engine/                      # 插件引擎
│   │   ├── Services/                    # 底层能力服务
│   │   ├── Capabilities/                # 能力接口定义
│   │   └── UI/                          # 主界面
│   ├── *Plugin/                         # DLL 插件
│   └── */                               # WebView 插件
├── docs/                                # 开发文档
├── tests/                               # 单元测试
├── 启动.bat                             # 一键启动脚本
├── 开发模式.bat                         # 开发模式脚本
└── 打包发布.bat                         # 打包脚本
```

---

## 🛠️ 技术栈

### 核心技术
- **.NET 8.0** - 现代化框架
- **WPF** - 原生 Windows UI
- **WebView2** - Edge Chromium 内核
- **Roslyn** - C# 脚本引擎

### UI 框架
- **WPF UI** - 现代化 UI 控件
- **HTML/CSS/JS** - WebView 插件

### 工具库
- **Serilog** - 日志系统
- **Newtonsoft.Json** - JSON 处理
- **Microsoft.CodeAnalysis** - 脚本编译

---

## 📝 开发插件

### WebView 插件示例

```javascript
// manifest.json
{
  "id": "com.example.myplugin",
  "name": "我的插件",
  "entry_point": "index.html",
  "runtime": "webview",
  "capabilities": ["system.clipboard"]
}
```

```html
<!-- index.html -->
<!DOCTYPE html>
<html>
<body>
  <h1>我的插件</h1>
  <button onclick="copyText()">复制文本</button>
  
  <script>
    async function copyText() {
      await window.hostApi.clipboard.setText('Hello World');
    }
  </script>
</body>
</html>
```

### DLL 插件示例

```csharp
public class MyPlugin : IPlugin
{
    public void Initialize(IHostApi hostApi)
    {
        hostApi.Hotkey.Register("Ctrl+Shift+M", OnHotkeyPressed);
    }
    
    private void OnHotkeyPressed()
    {
        // 插件逻辑
    }
}
```

更多示例请查看 [插件开发指南](docs/插件开发指南.md)

---

## 📊 版本历史

### v1.8.0 (2026-07-16)
- ✨ 新增壁纸管理能力
- ✨ 新增屏幕亮度控制
- ✨ 新增网络流量监控
- 🐛 修复批处理文件编码问题

### v1.7.0 (2026-07-16)
- ✨ 新增电源管理能力
- ✨ 新增主题管理能力

### v1.6.1 (2026-07-16)
- ✨ 新增 8 项底层能力
- ✨ 新增 5 个实用插件
- ✨ 新增自动化脚本系统
- 📝 美化 README 文档

[查看完整更新日志](docs/)

---

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

---

## 📄 开源协议

本项目采用 [MIT License](LICENSE) 开源协议。

---

## 🙏 致谢

- [WPF UI](https://github.com/lepoco/wpfui) - 现代化 WPF 控件库
- [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) - Edge Chromium 内核
- [Serilog](https://serilog.net/) - 结构化日志库

---

## 📧 联系方式

- **作者**: Kiro
- **项目地址**: https://github.com/Longyuyeee/Long_BetterWindows
- **问题反馈**: [GitHub Issues](https://github.com/Longyuyeee/Long_BetterWindows/issues)

---

<div align="center">

**⭐ 如果这个项目对你有帮助，请给一个 Star 支持一下！⭐**

Made with ❤️ by Kiro

</div>
