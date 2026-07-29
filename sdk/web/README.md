# @long-assistant/plugin-sdk

Long助手 Web 插件 API v1.0.0 的 TypeScript 合同与确定性 Mock Host。

## 使用类型

```json
{
  "devDependencies": {
    "@long-assistant/plugin-sdk": "file:../../sdk/web"
  }
}
```

纯 HTML/JavaScript 插件可以在项目的 `jsconfig.json` 中加载全局类型：

```json
{
  "compilerOptions": {
    "checkJs": true,
    "types": ["@long-assistant/plugin-sdk"]
  }
}
```

TypeScript 插件也可以显式引用：

```ts
import type { LongApi, LongResult } from "@long-assistant/plugin-sdk";

async function readClipboard(api: LongApi = long): Promise<string> {
  const response: LongResult<string | null> = await api.clipboard.getText();
  if (!response.success) throw new Error(response.error ?? "Clipboard failed");
  return response.data ?? "";
}
```

SDK 类型描述宿主真实桥接返回值。多数 API 返回
`{ success, data?, error? }`；文件列表、截图和下载保留生产桥接的专用字段。
Web 插件仍必须在 `manifest.json` 声明相应 capability，类型通过不等于获得权限。

## Mock Host

```ts
import { createLongMock, ok } from "@long-assistant/plugin-sdk/mock";

const mock = createLongMock({
  clipboardText: "fixture",
  storage: { revision: "v1" },
  handlers: {
    "http.get": async url => ok(`response from ${url}`)
  }
});

mock.install();

await long.clipboard.setText("updated");
mock.emitHotkey("Alt+X");

console.log(mock.getCalls("clipboard.setText"));
```

Mock Host 不访问真实剪贴板、文件、注册表、网络或系统设置。未配置的方法默认返回
`{ success: true }`，适合单元测试调用编排；需要业务数据时应显式提供 handler。
`storage.compareExchange`、剪贴板文本、热键回调和剪贴板事件具备确定性内存实现。

## 验证

```powershell
cd sdk/web
npm ci
npm test
```

- `test:types` 使用严格 TypeScript 模式编译示例；
- `test:mock` 使用 Node 内置测试运行器验证状态、调用记录、handler 和事件；
- 仓库 C# 质量门禁会把 Mock Host 的 `BRIDGE_METHODS` 与宿主注入脚本逐项对账。
