# Long窗口·全能助手

> 打造极致优雅、深度集成的 Windows 效率增强中枢。

Long窗口 是一个运行在 Windows 原生系统之上的轻量级效率工具平台。产品交互参考 uTools 的统一入口与插件化工作流，视觉采用 Long 自有的优雅、华丽、扁平、动效化设计语言。

当前主线版本为 **v1.9.0**，包含 27 个宿主能力接口、25 个内置插件，以及统一命令中心、超级面板和可信插件市场。当前开发状态见 [`docs/当前开发状态.md`](docs/当前开发状态.md)。

---

## 特性

- **插件驱动架构** — 宿主提供原子级系统能力（热键、Shell 感知、NTFS ADS、注册表），插件实现具体功能
- **文件夹备注助手** — 为任意文件夹添加隐藏备注，数据存入 NTFS 备用数据流，完全不可见
- **统一搜索与快捷启动** — 混排命令、开始菜单应用、Windows 设置和常用目录深层文件，并提供主/次级快捷动作
- **宏录制器** — 录制鼠标点击序列并回放，支持循环执行
- **Long Design System** — 优雅的亮暗主题、扁平层级、克制光影与语义化动效
- **统一命令中心** — 使用 `Alt + Space` 搜索并执行全部插件能力
- **系统托盘** — 关闭窗口后台运行，热键始终可用
- **插件热重载** — 修改插件 DLL 自动检测并重载，无需重启
- **分层权限边界** — Web 插件的宿主 API、页面导航与消息桥受能力和本地来源约束；DLL / C# 脚本属于本地完全信任扩展
- **原子化回滚** — 卸载插件时自动清除所有系统修改
- **离线优先** — 核心功能不依赖网络

---

## 快速开始

### 用户环境要求

- Windows 10/11
- 自包含包：无需安装 .NET
- 精简包：需要 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)
- Web 插件需要 Microsoft Edge WebView2 Runtime

推荐普通用户下载 `self-contained` 自包含便携包。安装、升级、卸载和校验方法见 [`docs/安装升级与卸载.md`](docs/安装升级与卸载.md)。

Windows 包采用公开的未签名分发通道；首次下载或启动时可能出现“未知发布者”或 SmartScreen 提示。请只从本仓库 Release 下载，并在运行前按发布页提供的 `SHA256SUMS.txt` 校验文件。Windows Authenticode 签名保留为未来可选增强，不作为当前发布阻塞项。

当前测试版本：[v1.9.0-rc.1 unsigned prerelease](https://github.com/Longyuyeee/Long_BetterWindows/releases/tag/v1.9.0-rc.1)。它用于真实下载与干净 Windows 验收，不是最终正式版本。

### 开发环境要求

- .NET 8 SDK 或更高版本

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
| 打开统一命令中心 | `Alt + Space`，冲突时 `Ctrl + Alt + Space` |
| 文件夹备注 | `Alt + M` |
| 宏录制 开始/停止 | `F6` |
| 宏回放 单次 | `F7` |
| 宏回放 循环 | `F8` |
| 全屏截图 | `Ctrl + Shift + S` |

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
│  │ Web 能力边界 · 热重载 · 配置持久化 │  │
│  └───────────────┬───────────────────┘  │
│                  │ ILongPlugin           │
│  ┌───────────────┴───────────────────┐  │
│  │  插件层 (Plugins)                 │  │
│  │  FolderNote │ QuickLaunch │ Macro │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

### 宿主能力 API（27 项）

| 接口 | 能力 ID | 说明 |
|---|---|---|
| `IHotKeyService` | `system.hotkey` | 全局热键注册/冲突/所有权 |
| `IClipboardService` | `system.clipboard` | 剪贴板读写 |
| `IShellSelectionService` | `shell.selection` | Explorer 文件夹感知/选中项坐标 |
| `IADSService` | `fs.ads.access` | NTFS 备用数据流读写 |
| `IRegistryService` | `system.registry.*` | 安全注册表操作 + 回滚 |
| `IStorageService` | `storage.local` | 本地 Key-Value 持久化存储 |
| `INotificationService` | `system.notification` | Toast 通知 |
| `IFileOpsService` | `file.ops` | 文件复制/移动/删除 |
| `IWindowInfoService` | `window.info` | 前台窗口信息/可见窗口列表 |
| `IScreenCaptureService` | `system.screenshot` | 全屏截图 |
| `IInputService` | `system.input` | 模拟按键/鼠标点击 |
| `IProcessService` | `system.process` | 进程启动/列表/终止 |
| `IPerformanceService` | `system.performance` | CPU、内存、磁盘与进程性能 |
| `INetworkPortService` | `network.ports` | TCP/UDP 端口与占用进程 |
| `INetworkMonitorService` | `network.monitor` | 网络接口、流量与速度 |
| `IHttpService` | `network.http` | 受控 HTTP 请求与下载 |
| `IFileSystemService` | `filesystem.advanced` | 搜索、哈希、分类、批量重命名 |
| `ICacheService` | `system.cache` | 系统缓存统计与清理 |
| `IScheduleService` | `system.schedule` | 计划任务管理 |
| `IAudioService` | `system.audio` | 音量、静音与音频设备 |
| `IPowerService` | `system.power` | 锁定、睡眠、休眠与电源状态 |
| `IThemeService` | `system.theme` | 系统主题与强调色 |
| `IWallpaperService` | `system.wallpaper` | 壁纸与显示样式 |
| `IBrightnessService` | `display.brightness` | 屏幕亮度控制 |
| `IPinyinService` | `text.pinyin` | 拼音转换、首字母与匹配 |
| `IUICapability` | `ui.window` | 插件窗口和交互控件 |

---

## 插件开发

### 三种方式，自由选择

| 方式 | 语言 | 编译 | 适合 |
|---|---|---|---|
| **HTML/JS** | HTML+CSS+JS | 不需要 | Web 开发者、富 UI 工具 |
| **C# 脚本** | C# (.csx) | 不需要 | 快速自动化 |
| **DLL 插件** | C# (.NET) | 需要 | 复杂系统插件 |

### HTML/JS 插件 — 1 分钟上手

```html
<!-- index.html -->
<button onclick="go()">备注此文件夹</button>
<script>
async function go() {
  let f = await long.shell.getActiveFolder();
  let t = await long.clipboard.getText();
  await long.fs.ads.write(f.data, t.data);
  long.ui.showToast('已保存！');
}
</script>
```

### 打包分发

```powershell
.\pack-plugin.ps1 -PluginDir "src/MyPlugin"   # → dist/MyPlugin-v1.0.0.lpak
```

用户拖放 `.lpak` 文件到 ToolCenter 即完成安装。

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
│   │   ├── Services/              #   能力与平台服务实现
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

## 版本历史

| 版本 | 日期 | 内容 |
|---|---|---|
| v0.1.0 | 2026-03 | 宿主框架 + 5 项能力 + 插件引擎 |
| v0.2.0 | 2026-06 | 12 项能力 + 16 插件 + 3 运行时 + 内置 IDE + 30 测试 |
| v0.5.0-rc.1 | 2026-07 | 统一命令中心 + Long Design System + 16 插件 UI 迁移 + 质量与发布基线 |
| v1.8.0 | 2026-07 | 27 个宿主能力接口 + 21 个内置插件 |
| v1.9.0 | 2026-07 | 合并交互平台、超级面板、可信插件市场与 25 插件发布基线 |

## 路线图

### 已完成
- [x] 插件引擎（扫描/加载/热重载/Web 能力边界与本地高信任提示）
- [x] 三种运行时（DLL / .csx 脚本 / WebView2 HTML/JS）
- [x] 27 个宿主能力接口
- [x] 25 个互不重复的内置插件
- [x] 内置插件开发 IDE
- [x] 插件脚手架 + 5 种模板
- [x] .lpak 打包分发
- [x] 365 个自动化测试
- [x] 插件开发指南 + 内置文档浏览器

### 进行中 / 预留
- [x] v1.9 统一上下文快照（文本/URL/文件/文件夹/Explorer 选区）
- [x] v0.6 图片上下文与动态搜索基础设施（统一结果、超时、去重、增量刷新）
- [x] v0.6 快捷启动器真实动态数据源与二次搜索闭环
- [x] v0.6 固定、最近使用、本地智能排序与隐私清除入口
- [x] v0.6 首批结果性能复核与最终验收
- [x] v0.7 第一批声明式插件生命周期与兼容状态机
- [x] v0.7 第二批 Long 超级面板外壳与共享搜索闭环
- [x] v0.7 第三批智能/固定/最近分组、滚轮切组与拖拽排序
- [x] v0.7 第四批自定义文件夹分组、组间移动与插件窗口资源释放
- [x] v0.7 第五批嵌入/分离窗口会话、统一返回与安全鼠标手势
- [x] v1.9 Long 超级面板与完整插件生命周期
- [x] v1.9 Long 原生插件市场核心功能与可信分发链
- [ ] v1.9 真实 Registry/CDN 生产演练与正式凭据签核
- [ ] v1.9 之后的组合动作编排（权限确认、执行取消和报告时间线已接入，命令输入编辑与外部导入交互待开发）
- [ ] Win11 Sparse Package 一级右键菜单（appxmanifest 已预留）
- [ ] CI/CD（workflow 已就绪，待 Token 权限修复）
- [ ] 国际化（zh-CN / en-US 已完成，待 UI 集成）
- [x] 主题切换（Dark / Light / System）

后续交互平台路线见 [`docs/Long_交互平台演进路线_v0.6-v0.8.md`](docs/Long_交互平台演进路线_v0.6-v0.8.md)。Long 不计划兼容 uTools 插件；两者仅在统一入口、上下文匹配等产品交互层面相互参考。

---

## 许可证

MIT License

---

## 致谢

Long窗口 的统一入口与插件交互参考 uTools；视觉系统与实现为 Long 自有设计。
