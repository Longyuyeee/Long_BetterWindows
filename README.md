<div align="center">

<img src="Assets/app-icon.png" width="128" alt="Long助手图标">

# Long助手

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
[开发状态](docs/当前开发状态.md) ·
[v1.11 稳定版收尾计划](docs/v1.11.0稳定版收尾开发审计与验收总计划_2026-08-13.md) ·
[总审计与后续计划](docs/开发总审计与后续计划_2026-08-02.md) ·
[收口执行交接](docs/收口执行交接_2026-08-01.md)

</div>

---

## 为什么是 Long助手

Long助手不是 uTools 插件兼容层。我们参考的是成熟的统一入口、上下文感知和插件化交互逻辑，并在自己的 Windows 原生能力、权限模型、视觉语言与工作流体系上继续扩展。

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

- 普通用户优先下载 `LongAssistant-Setup-*.exe`：无需管理员权限，支持开始菜单、可选桌面快捷方式、升级覆盖和标准卸载
- 需要免安装使用时选择 `self-contained.zip`：已包含 .NET 运行时
- 已安装 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0) 的便携用户可选择体积更小的 `framework-dependent.zip`
- Web 插件需要 Microsoft Edge WebView2 Runtime

当前源码版本为 `v1.11.0-rc.17`。RC16 已完成视觉、候选资产、桌面、安装器、任务栏与托盘自动门禁，但候选生成后的审计文档使正式人工回执入口按不可变策略判定其过期；RC17 仅重建同一产品代码与最新审计基线，不新增功能。冻结的 `rc.2` 至 `rc.16` 均不可覆盖。当前并非最终稳定版。Windows 安装包暂未使用 Authenticode 商业证书；首次运行可能出现 SmartScreen 提示，请核对 SHA-256。

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

### 一键启动与打包

在仓库根目录可以直接双击：

- `启动.bat`：构建 Release 并启动 Long助手；
- `开发模式.bat`：以 Debug 模式运行并保留控制台；
- `打包发布.bat`：自动读取项目版本，执行完整测试和发布流水线，生成 Setup.exe、两种便携 ZIP、Manifest 与 SHA-256，并打开产物目录。

对应的可配置 PowerShell 入口：

```powershell
.\start-long.ps1
.\start-long.ps1 -Configuration Debug -Wait
.\package-long.ps1 -PreflightOnly
.\package-long.ps1 -PackageKind All -OpenOutput
```

正式打包默认要求跟踪工作区干净。同版本目录已存在时会先询问是否替换；脚本不会静默删除旧产物。`-AllowDirty` 只适合本地测试包，不能用于发布。

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
.\validate-plugin.ps1 -Path "src/MyPlugin"
.\pack-plugin.ps1 -PluginDir "src/MyPlugin"
.\verify-plugin-runtime-matrix.ps1
```

打包器会先后两次复用生产验证规则，并生成带逐文件 SHA-256 总账的确定性 `.lpak`；相同输入可得到相同包哈希，缓存、临时文件和常见秘密文件会在发布前被阻断。
四运行时矩阵会真实构建、验证并打包 Web、C# Script、Native 和 Hybrid 样板，同时审计权限摘要、本地导入资格、高信任提示及远程市场资格。

继续阅读 [插件开发指南](docs/插件开发指南.md)、[宿主能力 API 手册](docs/基础能力_API_手册.md) 与 [Long Design System 使用指南](docs/Long_Design_System_使用指南.md)。
插件 Manifest 的机器可读权威契约见 [JSON Schema](schemas/plugin-manifest.schema.json)。
Web 插件可以直接使用 [`@long-assistant/plugin-sdk`](sdk/web/README.md) 的 API v1.0.0 TypeScript 声明和 Mock Host，在不启动 WPF/WebView2 的情况下完成严格类型检查与单元测试。
原生插件使用 [`LongBetterWindows.PluginSdk`](src/LongBetterWindows.PluginSdk/README.md) v1.0.0 编译，生命周期、命令合同和 27 项宿主能力已经从 WPF 宿主程序集拆出；新建原生模板不再引用整个宿主项目。
原生脚手架会同时生成 xUnit 合同测试项目，并使用 [`LongBetterWindows.PluginSdk.Testing`](sdk/dotnet/LongBetterWindows.PluginSdk.Testing/README.md) 在不启动 WPF 宿主的情况下验证身份、生命周期、能力拒绝、命令、语言和资源释放。Native、C# Script、Web 三类 CI 样板位于 [`sdk/ci`](sdk/ci/README.md)。

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
src/LongBetterWindows.Host/   WPF 宿主、交互平台与能力实现
src/LongBetterWindows.PluginSdk/ 原生插件稳定合同 SDK
sdk/dotnet/                  原生插件无宿主测试工具
sdk/ci/                      三运行时 CI 样板
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
.\package-long.ps1 -PackageKind All -OpenOutput
```

项目使用自动化单元测试、真实宿主启动与命令冒烟、WebView2 退出清理、插件数量与命令契约、双语键集合、视觉矩阵、DPI 与辅助功能门禁。v1.11 候选继承 [v1.10 开发审计与发布交接](docs/v1.10开发审计与发布交接_2026-07-26.md) 中尚未关闭的外部门禁。

## 设计与发布原则

- 核心能力离线优先，用户数据默认保留在本机
- Web 插件受能力声明与来源边界约束；DLL / C# 插件视为本地高信任扩展
- 系统修改必须可追踪、可撤销，危险工作流执行前必须审查
- uTools 仅作为产品交互参考，不提供其插件兼容性
- 预发布版不冒充稳定版；外部门禁与 Windows 发布者身份状态公开记录

## 文档导航

- [v1.11.0 稳定版收尾开发、审计与验收总计划](docs/v1.11.0稳定版收尾开发审计与验收总计划_2026-08-13.md)
- [最新开发总审计、目标偏移与后续计划](docs/开发总审计与后续计划_2026-08-02.md)
- [当前开发状态](docs/当前开发状态.md)
- [历史收口计划（2026-07-30）](docs/开发现状审计与后续收口计划_2026-07-30.md)
- [用户指南](docs/USER_GUIDE.md)
- [插件开发指南](docs/插件开发指南.md)
- [插件开发体系审计与优化计划](docs/插件开发体系审计与优化计划_2026-07-30.md)
- [Plugin Manifest JSON Schema](schemas/plugin-manifest.schema.json)
- [Web Plugin SDK v1.0.0](sdk/web/README.md)
- [.NET Plugin SDK v1.0.0](src/LongBetterWindows.PluginSdk/README.md)
- [.NET Plugin WPF UI Kit v1.0.0](src/LongBetterWindows.PluginSdk.Wpf/README.md)
- [.NET Plugin SDK Testing v1.0.0](sdk/dotnet/LongBetterWindows.PluginSdk.Testing/README.md)
- [Plugin CI templates](sdk/ci/README.md)
- [宿主能力 API 手册](docs/基础能力_API_手册.md)
- [API Reference](docs/API_REFERENCE.md)
- [UI / UX 设计与开发规范](docs/UI_UX_设计与开发规范_v1.0.md)
- [安全审计报告](docs/安全审计报告.md)
- [安全更新与签名发布](docs/安全更新与签名发布.md)
- [发布候选检查表](docs/发布候选检查表.md)

---

<div align="center">

**Long助手** — 让 Windows 的能力真正汇聚到一个入口。

</div>
