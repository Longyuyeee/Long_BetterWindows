# Long_BetterWindows 开发者文档

欢迎开发插件！本文档将指导你从零开始创建自己的 Windows 效率工具。

---

## 🚀 快速开始

### 最简单的插件（JavaScript）

创建 `hello.js`：

```javascript
// 注册热键
long.hotkey.register('Ctrl+Shift+H', async () => {
    await long.notification.show('Hello, Long_BetterWindows!', 'success');
});

console.log('✅ Hello 插件已加载');
```

将文件拖入应用，按 `Ctrl+Shift+H` 测试。**完成！**

---

## 📂 插件类型选择

Long_BetterWindows 支持 4 种插件类型：

### 1. JavaScript 单文件插件 ⚡

**适合**: 快速原型、简单工具、脚本自动化

**优点**:
- 零配置，拖入即用
- 热重载，修改即生效
- 浏览器调试工具支持

**示例**:
```javascript
// timestamp.js
long.hotkey.register('Alt+T', async () => {
    const time = new Date().toISOString();
    await long.clipboard.setText(time);
    await long.notification.show(`已复制: ${time}`, 'success');
});
```

---

### 2. TypeScript 单文件插件 🔷

**适合**: 需要类型安全、大型脚本

**优点**:
- 完整的类型提示
- 编译时错误检查
- 更好的 IDE 支持

**示例**:
```typescript
// note.ts
interface Note {
    id: string;
    content: string;
    created: number;
}

long.hotkey.register('Ctrl+N', async () => {
    const notes = await long.storage.get<Note[]>('notes') || [];
    const newNote: Note = {
        id: Date.now().toString(),
        content: '',
        created: Date.now()
    };
    notes.push(newNote);
    await long.storage.set('notes', notes);
});
```

---

### 3. C# 脚本插件 📜

**适合**: 需要 .NET 库、复杂逻辑、性能敏感

**优点**:
- 完整的 .NET 生态
- 更高的性能
- 类型安全

**示例**:
```csharp
// sysinfo.csx
using System;
using System.Diagnostics;

await Host.HotKey.RegisterAsync("Ctrl+Shift+I", async () =>
{
    var cpu = GetCpuUsage();
    var memory = GC.GetTotalMemory(false) / 1024 / 1024;
    await Host.Notification.ShowAsync(
        $"CPU: {cpu:F1}% | 内存: {memory}MB", "info");
});

double GetCpuUsage()
{
    // 实现 CPU 监控逻辑
    return 0.0;
}

Console.WriteLine("✅ 系统信息插件已加载");
```

---

### 4. DLL 插件 🏢

**适合**: 复杂应用、团队开发、需要第三方库

**优点**:
- 完整的项目结构
- NuGet 包管理
- 单元测试支持

**示例结构**:
```
MyPlugin/
├── manifest.json
├── MyPlugin.dll
├── MyPlugin.deps.json
└──依赖的其他 DLL
```

**代码**:
```csharp
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Contracts;

public class MyPlugin : ILongPlugin
{
    private IHostApi _host;

    public Task InitializeAsync(IHostApi hostApi)
    {
        _host = hostApi;
        
        _host.HotKey.RegisterAsync("Ctrl+M", async () =>
        {
            await _host.Notification.ShowAsync("DLL 插件运行中", "info");
        });
        
        return Task.CompletedTask;
    }

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}
```

---

## 📝 Manifest 配置详解

DLL 插件需要 `manifest.json`：

```json
{
  "id": "com.example.myplugin",
  "name": "我的插件",
  "version": "1.0.0",
  "description": "插件描述",
  "author": "作者名",
  "runtime": "dotnet",
  "entry": "MyPlugin.dll",
  "capabilities": [
    "system.hotkey",
    "system.notification",
    "system.clipboard"
  ],
  "dependencies": []
}
```

### 字段说明

- **id**: 唯一标识符，建议使用反向域名
- **name**: 显示名称
- **version**: 版本号（语义化版本）
- **runtime**: 运行时类型（dotnet / webview / csharp-script）
- **entry**: 入口文件
- **capabilities**: 需要的能力列表
- **dependencies**: 依赖的其他插件

### Capabilities 权限列表

```json
{
  "capabilities": [
    "system.clipboard",      // 剪贴板
    "system.hotkey",         // 热键
    "system.registry.write", // 注册表写入
    "system.registry.read",  // 注册表读取
    "network.http",          // HTTP 请求
    "system.screenshot",     // 截图
    "system.storage",        // 数据存储
    "system.shell.execute",  // Shell 执行
    "fs.ads.access",         // ADS 流访问
    "system.notification",   // 通知
    "system.theme",          // 主题
    "system.window",         // 窗口管理
    "system.process",        // 进程管理
    "system.audio",          // 音频控制
    "system.monitor",        // 多显示器
    "system.power",          // 电源管理
    "system.network",        // 网络状态
    "system.sensor"          // 传感器
  ]
}
```

---

## 🔌 Host API 使用指南

### JavaScript/TypeScript API

所有 API 通过全局对象 `long` 访问：

```javascript
// 剪贴板
await long.clipboard.setText('文本内容');
const text = await long.clipboard.getText();

// 热键
long.hotkey.register('Ctrl+A', async () => {
    console.log('热键触发');
});

// 通知
await long.notification.show('消息内容', 'success'); // success/info/warning/error

// 存储
await long.storage.set('key', 'value');
const value = await long.storage.get('key');

// HTTP 请求
const response = await long.http.get('https://api.example.com');
const data = await long.http.post('https://api.example.com', { data: 'value' });

// 截图
const base64 = await long.screenshot.capture();

// Shell 执行
await long.shell.openUrl('https://example.com');
await long.shell.openFile('C:\\file.txt');

// 进程管理
await long.process.start('notepad.exe');
const processes = await long.process.getList();
```

### C# API

通过 `Host` 对象访问：

```csharp
// 剪贴板
await Host.Clipboard.SetTextAsync("文本内容");
var text = await Host.Clipboard.GetTextAsync();

// 热键
await Host.HotKey.RegisterAsync("Ctrl+A", async () =>
{
    Console.WriteLine("热键触发");
});

// 通知
await Host.Notification.ShowAsync("消息内容", "success");

// 存储
await Host.Storage.SetAsync("key", "value");
var value = await Host.Storage.GetAsync("key");

// HTTP 请求
var response = await Host.Http.GetAsync("https://api.example.com");
var data = await Host.Http.PostAsync("https://api.example.com", jsonData);
```

---

## 🐛 调试技巧

### 1. 控制台输出

**JavaScript/TypeScript**:
```javascript
console.log('调试信息');
console.error('错误信息');
```

**C#**:
```csharp
Console.WriteLine("调试信息");
```

所有输出会显示在「开发工具」面板。

### 2. 热重载

修改插件文件后，应用会自动检测并重新加载。无需重启应用。

### 3. 错误处理

**JavaScript**:
```javascript
try {
    await long.http.get('https://invalid-url');
} catch (error) {
    console.error('请求失败:', error);
    await long.notification.show('操作失败', 'error');
}
```

**C#**:
```csharp
try
{
    await Host.Http.GetAsync("https://invalid-url");
}
catch (Exception ex)
{
    Console.WriteLine($"请求失败: {ex.Message}");
}
```

### 4. 断点调试

- **JavaScript**: 打开浏览器开发者工具，在 Sources 面板设置断点
- **C#**: 附加 Visual Studio 调试器到 Host 进程

---

## 📦 打包和发布

### 创建 .lpak 包

.lpak 是插件的标准打包格式（实际上是 ZIP 文件）：

```bash
# 创建插件目录结构
MyPlugin/
├── manifest.json
├── plugin.js (或 plugin.dll)
└── 其他资源文件

# 打包为 .lpak
zip -r MyPlugin.lpak MyPlugin/

# 或使用 PowerShell
Compress-Archive -Path MyPlugin/* -DestinationPath MyPlugin.lpak
```

### 发布到插件市场

1. 在 `market-data.json` 中添加插件信息
2. 上传 .lpak 文件到 CDN
3. 提交 Pull Request

---

## ✨ 最佳实践

### 1. 资源释放

**始终清理资源**:
```javascript
// 好的做法
long.hotkey.register('Ctrl+A', async () => {
    const file = await openFile();
    try {
        // 使用 file
    } finally {
        await file.close();
    }
});
```

### 2. 异步处理

**避免阻塞 UI**:
```javascript
// 不好 ❌
function syncOperation() {
    // 长时间同步操作
}

// 好 ✅
async function asyncOperation() {
    await long.http.get('...');
}
```

### 3. 错误提示

**给用户清晰的反馈**:
```javascript
try {
    await someOperation();
    await long.notification.show('操作成功', 'success');
} catch (error) {
    await long.notification.show(`操作失败: ${error.message}`, 'error');
}
```

### 4. 配置存储

**使用 storage API 持久化配置**:
```javascript
// 保存配置
await long.storage.set('config', {
    theme: 'dark',
    autoStart: true
});

// 读取配置
const config = await long.storage.get('config') || {};
```

---

## ⚠️ 常见错误

### 1. 权限错误

```
Error: 插件未声明权限: system.clipboard
```

**解决**: 在 manifest.json 中添加对应的 capability

### 2. 热键冲突

```
Error: 热键 Ctrl+A 已被占用
```

**解决**: 更换热键组合或检查其他插件

### 3. 异步操作未等待

```javascript
// 错误 ❌
long.clipboard.setText('text'); // 忘记 await

// 正确 ✅
await long.clipboard.setText('text');
```

---

## 📚 进一步学习

- [API 完整参考](API_REFERENCE.md)
- [示例插件](../examples/)
- [用户手册](USER_GUIDE.md)

---

**开始创造你的第一个插件吧！** 🚀
