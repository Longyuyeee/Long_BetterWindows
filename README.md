<div align="center">

# Long窗口 · 全能助手

### 一个优雅、华丽、扁平、灵动的 Windows 效率与插件平台

统一入口连接命令、应用、文件、系统能力与本地插件。

交互逻辑参考 uTools 的统一入口与插件化工作流，视觉和技术实现采用 Long 自有设计体系。

[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-1677ff?logo=windows11&logoColor=white)](https://github.com/Longyuyeee/Long_BetterWindows/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-7c3aed?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Release](https://img.shields.io/github/v/release/Longyuyeee/Long_BetterWindows?include_prereleases&label=release&color=8b5cf6)](https://github.com/Longyuyeee/Long_BetterWindows/releases)
[![Tests](https://img.shields.io/badge/tests-automated-10b981)](#开发与验证)

[下载最新版本](https://github.com/Longyuyeee/Long_BetterWindows/releases) ·
[用户指南](docs/USER_GUIDE.md) ·
[插件开发](docs/插件开发指南.md) ·
[开发状态](docs/当前开发状态.md)

</div>

---

## 为什么是 Long窗口

Long窗口不是 uTools 插件兼容层。我们参考的是成熟的统一入口、上下文感知和插件化交互逻辑，并在自己的 Windows 原生能力、权限模型、视觉语言与工作流体系上继续扩展。

| 体验方向 | 当前实现 |
|---|---|
| 一个入口 | `Alt + Space` 唤起命令中心，混排插件命令、应用、Windows 设置、常用目录与文件 |
| 上下文感知 | 支持文本、URL、文件、文件夹、图片及 Explorer 选区上下文 |
| 插件平台 | DLL、C# 脚本和 HTML/JS 三种运行时，统一能力 API 与生命周期 |
| 超级面板 | 鼠标手势呼出、智能分组、固定、最近使用和拖拽排序 |
| 工作流 | 多步骤组合、参数 Schema、执行审查、终端输出和安全导出 |
| 原生集成 | 热键、托盘、NTFS ADS、右键菜单、进程、网络、窗口、输入与显示能力 |
| 可信分发 | 插件市场签名链、发布校验、应用更新清单签名与 SHA-256 下载验证 |

## 产品体验

### 统一命令中心

在同一个搜索框里定位并执行命令、应用、设置和文件。支持主操作、次级操作、键盘导航、动态结果和本地智能排序。

### Long Design System

亮色与暗色主题、清晰的扁平层级、克制的光影、语义化动效、响应式布局，以及高对比度、减少动画、键盘路径和屏幕阅读器语义。

### 内置插件生态

当前发布基线包含 **25 个内置插件、42 条命令、27 项宿主能力接口**，覆盖文件夹备注、快速启动、截图、窗口管理、剪贴板、文本处理、开发工具、硬件与网络等场景。

### 安全更新

软件启动后会自动发现当前更新通道的新版本。发布端使用仅保存在维护者本机的 RSA 私钥签署更新清单；客户端内置公钥，并在下载前后验证：

- 更新清单签名与发布版本一致性
- Release、资源地址和源码提交边界
- 更新包文件名、大小与 SHA-256
- 临时文件完整性，校验失败不会留下可安装包

更新包验证通过后仍由用户确认安装，不静默执行来自网络的程序。

## 快速开始

### 下载

前往 [GitHub Releases](https://github.com/Longyuyeee/Long_BetterWindows/releases)：

- 普通用户选择 `self-contained`：已包含 .NET 运行时
- 已安装 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0) 的用户可选择 `framework-dependent`
- Web 插件需要 Microsoft Edge WebView2 Runtime

当前公开测试通道为 `v1.10.0-rc`。它用于真实下载、干净 Windows 和外部门禁验收，并非最终稳定版。Windows 安装包暂未使用 Authenticode 商业证书；首次运行可能出现 SmartScreen 提示，请只从本仓库 Release 下载。

完整安装、升级、校验和卸载说明见 [安装升级与卸载](docs/安装升级与卸载.md)。

### 常用操作

| 操作 | 快捷键 |
|---|---|
| 打开统一命令中心 | `Alt + Space` |
| 备用命令中心热键 | `Ctrl + Alt + Space` |
| 文件夹备注 | `Alt + M` |
| 宏录制开始 / 停止 | `F6` |
| 宏回放一次 / 循环 | `F7` / `F8` |
| 全屏截图 | `Ctrl + Shift + S` |

## 插件开发

| 运行时 | 开发体验 | 适合场景 |
|---|---|---|
| HTML / JS | 无需编译，内置 WebView2 | 富交互小工具 |
| C# Script | 单文件、快速迭代 | 自动化与能力组合 |
| .NET DLL | 强类型、完整性能 | 复杂原生插件 |

最小 Web 插件示例：

```html
<button onclick="saveNote()">备注当前文件夹</button>
<script>
async function saveNote() {
  const folder = await long.shell.getActiveFolder();
  const text = await long.clipboard.getText();
  await long.fs.ads.write(folder.data, text.data);
  long.ui.showToast("已保存");
}
</script>
```

创建和打包：

```powershell
.\new-plugin.ps1
.\pack-plugin.ps1 -PluginDir "src/MyPlugin"
```

继续阅读 [插件开发指南](docs/插件开发指南.md)、[宿主能力 API 手册](docs/基础能力_API_手册.md) 与 [Long Design System 使用指南](docs/Long_Design_System_使用指南.md)。

## 架构

```text
统一命令中心 / 超级面板 / ToolCenter / 插件窗口
                        │
             上下文、搜索与工作流协调层
                        │
              插件引擎与可信市场链路
         ┌──────────────┼──────────────┐
       DLL            C# Script       WebView2
         └──────────────┼──────────────┘
                        │
     27 项 Windows 原子能力与本地存储 / 回滚服务
```

核心目录：

```text
src/LongBetterWindows.Host/   WPF 宿主、交互平台、能力与服务
src/*Plugin/                  内置原生与 Web 插件
src/Templates/                插件脚手架模板
tests/LongBetterWindows.Tests 自动化测试与质量门禁
docs/                         设计、开发、发布和验收文档
```

## 开发与验证

环境要求：Windows 10/11 与 .NET 8 SDK。

```powershell
git clone https://github.com/Longyuyeee/Long_BetterWindows.git
cd Long_BetterWindows
dotnet build LongBetterWindows.sln
dotnet test tests/LongBetterWindows.Tests
dotnet run --project src/LongBetterWindows.Host
```

构建发布候选：

```powershell
.\release.ps1 -Version 1.10.0-rc.3
```

项目使用自动化单元测试、真实宿主启动与命令冒烟、WebView2 退出清理、插件数量与命令契约、双语键集合、视觉矩阵、DPI 与辅助功能门禁。发布状态和仍需外部环境完成的项目见 [v1.10 开发审计与发布交接](docs/v1.10开发审计与发布交接_2026-07-26.md)。

## 设计与发布原则

- 核心能力离线优先，用户数据默认保留在本机
- Web 插件受能力声明与来源边界约束；DLL / C# 插件视为本地高信任扩展
- 系统修改必须可追踪、可撤销，危险工作流执行前必须审查
- uTools 仅作为产品交互参考，不提供其插件兼容性
- 预发布版不冒充稳定版；外部门禁与 Windows 发布者身份状态公开记录

## 文档导航

- [用户指南](docs/USER_GUIDE.md)
- [开发者指南](docs/DEVELOPER_GUIDE.md)
- [API Reference](docs/API_REFERENCE.md)
- [UI / UX 设计与开发规范](docs/UI_UX_设计与开发规范_v1.0.md)
- [安全审计报告](docs/安全审计报告.md)
- [安全更新与签名发布](docs/安全更新与签名发布.md)
- [发布候选检查表](docs/发布候选检查表.md)
- [当前开发状态](docs/当前开发状态.md)

---

<div align="center">

**Long窗口** — 让 Windows 的能力真正汇聚到一个入口。

</div>
