# Long窗口·全能助手

> 打造极致优雅、深度集成的 Windows 效率增强中枢。

Long窗口 是一个运行在 Windows 原生系统之上的轻量级效率工具平台，通过 Apple 风格的视觉语言和高度可扩展的插件机制，解决 Windows 原生功能滞后或难以使用的痛点。

---

## 特性

- **插件驱动架构** — 宿主提供原子级系统能力（热键、Shell 感知、NTFS ADS、注册表），插件实现具体功能
- **文件夹备注助手** — 为任意文件夹添加隐藏备注，数据存入 NTFS 备用数据流，完全不可见
- **快捷启动器** — 全局搜索已安装应用，一键启动
- **宏录制器** — 录制鼠标点击序列并回放，支持循环执行
- **Apple 风格 UI** — 大圆角、弹性动效、半透明浮窗、暗色主题
- **系统托盘** — 关闭窗口后台运行，热键始终可用
- **插件热重载** — 修改插件 DLL 自动检测并重载，无需重启
- **权限沙盒** — 插件只能使用 manifest.json 中声明的能力
- **原子化回滚** — 卸载插件时自动清除所有系统修改
- **离线优先** — 核心功能不依赖网络

---

## 快速开始

### 环境要求

- Windows 10/11
- .NET 8.0 SDK

### 构建运行

```powershell
git clone https://github.com/Longyuyeee/Long_BetterWindows.git
cd Long_BetterWindows
dotnet build
dotnet run --project src/LongBetterWindows.Host
```

### 使用方式

| 操作 | 快捷键 |
|---|---|
| 文件夹备注 | `Alt + M` |
| 快捷启动器 | `Ctrl + Shift + Space` |
| 宏录制 开始/停止 | `F6` |
| 宏回放 单次 | `F7` |
| 宏回放 循环 | `F8` |

也可以通过**右键文件夹 → 备注此文件夹**触发。

---

## 架构

```
┌─────────────────────────────────────────┐
│  宿主 (Host)                             │
│  ┌───────────────────────────────────┐  │
│  │  能力服务层 (Capability Services)  │  │
│  │  HotKey │ Shell │ ADS │ Registry  │  │
│  │  Storage │ Rollback │ Tray       │  │
│  └───────────────┬───────────────────┘  │
│                  │ IHostApi              │
│  ┌───────────────┴───────────────────┐  │
│  │  插件引擎 (Plugin Engine)         │  │
│  │  Scanner → Loader → Registry      │  │
│  │  权限沙盒 · 热重载 · 配置持久化    │  │
│  └───────────────┬───────────────────┘  │
│                  │ ILongPlugin           │
│  ┌───────────────┴───────────────────┐  │
│  │  插件层 (Plugins)                 │  │
│  │  FolderNote │ QuickLaunch │ Macro │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

### 宿主能力 API

| 接口 | 能力 | 说明 |
|---|---|---|
| `IHotKeyService` | `system.hotkey` | 全局热键注册、冲突检测 |
| `IShellSelectionService` | `shell.selection` | Explorer 文件夹感知、选中项坐标 |
| `IADSService` | `fs.ads.access` | NTFS 备用数据流读写 |
| `IRegistryService` | `system.registry.*` | 安全注册表操作 + 回滚 |
| `IStorageService` | `storage.local` | 本地 Key-Value 持久化存储 |

---

## 插件开发

### 脚手架一键创建

```powershell
.\new-plugin.ps1 -Name "我的插件" -Id "com.example.myplugin" -Template hotkey
```

### 三级模板

| 模板 | 说明 |
|---|---|
| `empty` | 最小骨架，仅实现生命周期 |
| `hotkey` | 注册全局热键 + 能力调用 |
| `full` | 多能力 + 自定义设置 UI + 配置持久化 |

### 核心接口

```csharp
public class MyPlugin : ILongPlugin
{
    public string Id => "com.example.myplugin";
    public string Name => "我的插件";
    public string Version => "1.0.0";
    public PluginState State { get; private set; }

    public Task<bool> InitializeAsync(IHostApi host) { /* 获取能力 */ }
    public Task<bool> StartAsync() { /* 注册热键、启动任务 */ }
    public Task<bool> StopAsync() { /* 释放资源 */ }
}

// 可选：自定义设置面板
public class MyPlugin : ILongPlugin, IHasSettingsUI
{
    public FrameworkElement CreateSettingsUI() { /* 返回 WPF 面板 */ }
}
```

详细文档见 [`docs/插件开发指南.md`](docs/插件开发指南.md)

---

## 项目结构

```
Long_BetterWindows/
├── src/
│   ├── LongBetterWindows.Host/    # 宿主应用 (WPF .NET 8)
│   │   ├── Core/                  #   插件接口契约
│   │   ├── Capabilities/          #   原子能力接口
│   │   ├── Contracts/             #   数据模型
│   │   ├── Engine/                #   插件引擎
│   │   ├── Services/              #   能力实现 (13 项服务)
│   │   └── Views/                 #   UI 组件
│   ├── SamplePlugin/              # 教学参考插件
│   ├── FolderNotePlugin/          # 文件夹备注插件
│   ├── QuickLaunchPlugin/         # 快捷启动器插件
│   ├── MacroPlugin/               # 宏录制器插件
│   └── Templates/                 # 插件开发模板 (3 级)
├── Plugins/                       # 插件输出目录
├── docs/                          # 设计 + 开发文档
└── new-plugin.ps1                 # 插件脚手架脚本
```

---

## 技术栈

| 技术 | 用途 |
|---|---|
| .NET 8.0 + WPF | 应用框架 |
| WPF-UI 3.0.4 | Fluent Design 主题 |
| Serilog | 结构化日志 |
| Win32 P/Invoke | 系统级集成 (热键、ADS、全局钩子) |
| COM Interop | Explorer Shell 感知 (IShellWindows) |

---

## 许可证

MIT License

---

## 致谢

Long窗口 的设计灵感来自 Apple 的设计语言和 uTools 的插件生态理念。
