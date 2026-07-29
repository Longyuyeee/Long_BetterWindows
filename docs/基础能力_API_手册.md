# Long助手基础能力 API 手册

> 当前 Web API 版本：`1.0.0`
> TypeScript 权威合同：`sdk/web/index.d.ts`
> 宿主运行时权威实现：`WebPluginBridgeProtocol.BuildInjectionScript`

本手册只描述宿主已经实现的能力。Web 插件的编辑器提示、参数类型和返回类型以
[`@long-assistant/plugin-sdk`](../sdk/web/README.md) 为准；Manifest 能力白名单以
[`plugin-manifest.schema.json`](../schemas/plugin-manifest.schema.json) 为准。

## 1. 合同与兼容性

- SDK 包版本与宿主 `ApiVersion.Current` 当前均为 `1.0.0`。
- Manifest 建议声明 `"min_api_version": "1.0.0"`。
- 同一主版本内，宿主 API 次版本不低于插件要求时兼容。
- 破坏性变更必须提升 API 主版本；弃用接口至少保留两个小版本。
- C# 接口、Web 注入、TypeScript 声明、Mock Host 和文档必须在同一批提交中更新。

仓库质量门禁会从宿主注入脚本提取全部 127 个 bridge method，并与 Mock Host 的
`BRIDGE_METHODS` 逐项对账。漏加、误删或拼写漂移会阻断测试。

## 2. 安装与类型检查

仓库内开发可以使用本地包：

```json
{
  "devDependencies": {
    "@long-assistant/plugin-sdk": "file:../../sdk/web"
  }
}
```

纯 JavaScript 插件使用 `jsconfig.json`：

```json
{
  "compilerOptions": {
    "checkJs": true,
    "types": ["@long-assistant/plugin-sdk"]
  }
}
```

TypeScript 可以导入命名类型，同时直接使用宿主注入的全局 `long`：

```ts
import type { LongResult } from "@long-assistant/plugin-sdk";

const response: LongResult<string | null> = await long.clipboard.getText();
if (!response.success) throw new Error(response.error ?? "读取失败");
console.log(response.data ?? "");
```

## 3. 返回值与错误处理

多数方法返回：

```ts
interface LongResult<T> {
  success: boolean;
  data?: T;
  error?: string | null;
}
```

调用 Promise 被 bridge 协议拒绝时会抛出异常；宿主能力执行失败通常返回
`success=false`。插件必须同时处理两条失败路径：

```ts
try {
  const response = await long.clipboard.getText();
  if (!response.success) throw new Error(response.error ?? "读取失败");
} catch (error) {
  await long.app.log("clipboard failed", error);
}
```

生产桥接保留少量专用形态：

- `shell.listFiles` 使用顶层 `files`；
- `screenshot.captureRegion` 和 `http.download` 使用顶层 `filePath`；
- `window.getForeground` 成功时直接返回窗口对象；
- C# 数据模型当前按 PascalCase 序列化。具体类型已在 `index.d.ts` 固定。

## 4. Web API 命名空间

| 命名空间 | 主要能力 | 常见 Manifest capability |
|---|---|---|
| `long.app` | 打开目标、通知、版本、日志 | `shell.execute` / `system.notification` |
| `long.clipboard` | 文本读写、清空、变化监听 | `system.clipboard` / `system.clipboard.monitor` |
| `long.shell` | Explorer 选区、文件列表/重命名、打开目标 | `shell.selection` / `file.ops` / `shell.execute` |
| `long.fs.ads` | NTFS ADS 读写、删除、探测 | `fs.ads.access` |
| `long.hotkey` | 注册、注销、冲突检查 | `system.hotkey` |
| `long.registry` | 注册表读写与删除 | `system.registry.write` |
| `long.storage` | 插件隔离 KV 与原子 CAS | 无需额外声明 |
| `long.process` | 进程启动、列表、终止 | `system.process` |
| `long.fileOps` | 文件复制、移动、删除、存在检查 | `file.ops` |
| `long.performance` | CPU、内存、磁盘、系统和进程排行 | `system.performance` |
| `long.networkPort` | TCP/UDP 端点和占用进程 | `network.ports` |
| `long.network` | 网络统计、实时速度、接口 | `network.monitor` |
| `long.audio` | 音量、静音和设备 | `system.audio` |
| `long.power` | 电源状态、锁屏、睡眠、关机 | `system.power` |
| `long.theme` | 系统主题和强调色 | `system.theme` |
| `long.wallpaper` | 壁纸路径和样式 | `system.wallpaper` |
| `long.brightness` | 屏幕亮度 | `display.brightness` |
| `long.pinyin` | 拼音、首字母、匹配和过滤 | `text.pinyin` |
| `long.input` | 按键和鼠标输入 | `system.input` |
| `long.fileSystem` | 高级枚举、哈希、搜索、整理 | `filesystem.advanced` |
| `long.cache` | 缓存统计和清理 | `system.cache` |
| `long.schedule` | 定时任务管理 | `system.schedule` |
| `long.ui` | Toast、对话框和子窗口 | `system.notification` / `ui.window` |
| `long.screenshot` | 全屏与区域截图 | `system.screenshot` |
| `long.http` | GET、POST 和下载 | `network.http` |
| `long.window` | 前台窗口和可见窗口 | `window.info` |

完整方法、参数、枚举和数据模型见
[`sdk/web/index.d.ts`](../sdk/web/index.d.ts)。不要从本表推断全部能力；新增或修改
Manifest 时读取 JSON Schema。

## 5. 事件

剪贴板监听：

```ts
await long.clipboard.startMonitoring(event => {
  console.log(event.content_type, event.text, event.timestamp);
});

// 页面停止前配对释放
await long.clipboard.stopMonitoring();
```

语言变化、命令调用和热键属于 WebView 消息协议。SDK 提供
`LongLanguageChangedMessage`、`LongCommandMessage`、`LongClipboardChangedEvent`
和 `LongHostMessage` 类型。语言监听示例：

```ts
window.chrome?.webview?.addEventListener("message", ({ data }) => {
  if (typeof data !== "object" || data === null) return;
  if ((data as { type?: string }).type !== "long.language-changed") return;
  // 使用 LongLanguageChangedMessage 读取 resolved_language/resources
});
```

## 6. Mock Host

Mock Host 不需要启动 WPF 或 WebView2：

```ts
import {
  createLongMock,
  ok
} from "@long-assistant/plugin-sdk/mock";

const mock = createLongMock({
  clipboardText: "fixture",
  storage: { revision: "v1" },
  handlers: {
    "http.get": async url => ok(`fixture:${url}`)
  }
});

mock.install();
await long.clipboard.setText("updated");

console.log(mock.getCalls("clipboard.setText"));
```

默认方法只返回确定性成功结果，不触碰真实系统。需要数据的能力应显式设置 handler。
Mock 内置剪贴板文本、隔离存储、`compareExchange`、热键回调和剪贴板事件。

```powershell
cd sdk/web
npm ci
npm test
```

## 7. 权限与安全边界

- TypeScript 编译成功不代表 Manifest 已声明权限。
- Web bridge 在每次调用前检查 capability；未声明能力会返回拒绝。
- `long.storage` 数据按插件 ID 隔离。
- Mock Host 不模拟 Windows ACL、UAC、Explorer、WebView2 或真实并发时序。
- C# Script、Native 和 Hybrid 属于完全信任代码；本 Web SDK 不改变其安全边界。
- 网络、文件写入、进程终止、输入注入和电源操作必须在真实宿主中继续做人工验收。

## 8. API 变更流程

1. 在 C# capability 和 `WebPluginHostDispatcher` 实现行为。
2. 在 `BuildInjectionScript` 暴露方法并绑定正确 capability。
3. 更新 `sdk/web/index.d.ts`、Mock Host、Node 类型/行为测试。
4. 更新 Manifest Schema 或能力说明（如需要）。
5. 执行 `npm test`、C# 全量测试、Release 构建和四运行时矩阵。
6. 在审计文档记录 API 版本、兼容性和迁移方式。
