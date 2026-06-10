# Long_BetterWindows 设计文档 (Part 4: 核心 API 接口与技术方案细节)

## 1. 深度系统集成：文件夹备注助手的原子化拆解

为了实现“文件夹备注助手”的专业性，主程序需要开放以下底层的 **原子级能力 (Atomic Capabilities)**。这些能力虽然由备注助手首发，但将作为通用接口开放给后续所有插件。

### A. 视图状态注入 (View State Injection)
*   **目标：** 一键开启资源管理器的“备注”列。
*   **技术路径：** 
    1. 主程序通过注册表定位 `HKEY_CURRENT_USER\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags`。
    2. 修改对应 FolderTypes 的 `ColumnList`，注入 `System.Comment` 属性。
    3. 监听 `SHChangeNotify` 强制资源管理器刷新视图，避免用户重启电脑。

### B. 隐身存储方案：NTFS ADS (Alternate Data Streams)
*   **目标：** 实现备注数据与文件的“零污染”绑定。
*   **技术路径：** 
    1. 利用 NTFS 特性，在文件/文件夹后附加隐藏数据流（如 `folder_path:long_note`）。
    2. 该流不占用常规文件大小统计，且在 Windows 资源管理器中完全不可见。
    3. 宿主程序提供 `Read/Write` 封装，确保在非 NTFS 分区（如 FAT32）时自动回退到 `desktop.ini` 方案。

### C. 资源管理器坐标感知 (Coordinate Awareness)
*   **目标：** 浮动输入框精准出现在选中的文件上方。
*   **技术路径：** 
    1. 利用 COM 接口 `IShellWindows` 锁定当前活跃的资源管理器窗口。
    2. 通过 `IFolderView` 获取选中项的 Index。
    3. 调用 `GetItemRect` 换算屏幕物理坐标。
    4. 宿主程序将此坐标传递给浮动 UI 引擎，实现“吸附式”弹出。

---

## 2. Windows 11 一级菜单注入策略

*   **痛点：** Win11 默认将自定义右键菜单折叠。
*   **方案：** 主程序将注册为 **稀疏包 (Sparse Package)**。
*   **优势：** 
    1. 宿主程序拥有有效的包标识符。
    2. 所有插件的菜单项统一由宿主进行“代理注册”。
    3. 插件只需要提交图标和文字，即可出现在 Win11 的第一级菜单中，无需每个插件都去处理复杂的签名问题。

---

## 3. 插件权限边界定义

备注助手（FolderNote）在 `manifest.json` 中必须显式声明以下权限：
- `system.registry.write`: 修改视图列显示。
- `shell.ui.locator`: 获取文件坐标。
- `fs.ads.access`: 读写隐藏备注流。

---

## 4. 本阶段关键备注
> **备注：** 所有的系统修改操作（注册表、ADS 写入）必须支持 **“原子化回滚”**。如果插件被卸载或禁用，主程序应能够通过日志记录自动清除该插件留下的所有系统痕迹，保持系统的纯净。
