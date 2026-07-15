# Long_BetterWindows API 完整参考

本文档提供所有 19 种能力 API 的完整参考。

---

## 📋 clipboard - 剪贴板

### setText(text: string): Promise<void>
设置剪贴板文本内容

**参数**:
- `text`: 要设置的文本内容

**示例**:
```javascript
await long.clipboard.setText('Hello, World!');
```

### getText(): Promise<string>
获取剪贴板文本内容

**返回**: 剪贴板中的文本

**示例**:
```javascript
const text = await long.clipboard.getText();
console.log('剪贴板内容:', text);
```

**权限**: `system.clipboard`

---

## ⌨️ hotkey - 全局热键

### register(hotkey: string, callback: Function): void
注册全局热键

**参数**:
- `hotkey`: 热键组合（如 "Ctrl+Shift+A"）
- `callback`: 触发时的回调函数

**支持的修饰键**: Ctrl, Shift, Alt, Win

**示例**:
```javascript
long.hotkey.register('Ctrl+Shift+T', async () => {
    await long.notification.show('热键触发！', 'info');
});
```

**权限**: `system.hotkey`

---

## 💾 storage - 数据存储

### set(key: string, value: any): Promise<void>
存储数据

**参数**:
- `key`: 键名
- `value`: 值（会自动序列化为 JSON）

**示例**:
```javascript
await long.storage.set('user', { name: 'Alice', age: 25 });
```

### get(key: string): Promise<any>
获取数据

**参数**:
- `key`: 键名

**返回**: 存储的值（自动反序列化）

**示例**:
```javascript
const user = await long.storage.get('user');
console.log(user.name); // 'Alice'
```

### delete(key: string): Promise<void>
删除数据

**权限**: `system.storage`

---

## 🔔 notification - 通知提示

### show(message: string, type: string): Promise<void>
显示通知

**参数**:
- `message`: 通知内容
- `type`: 类型（'success' | 'info' | 'warning' | 'error'）

**示例**:
```javascript
await long.notification.show('操作成功！', 'success');
await long.notification.show('请注意', 'warning');
```

**权限**: `system.notification`

---

## 📝 registry - 注册表

### read(key: string): Promise<string>
读取注册表键值

**参数**:
- `key`: 相对路径（基于 `Software\LongBetterWindows\`）

**示例**:
```javascript
const value = await long.registry.read('MyApp\\Settings');
```

### write(key: string, value: string): Promise<void>
写入注册表键值

**权限**: `system.registry.read` / `system.registry.write`

---

## 🌐 http - HTTP 请求

### get(url: string, headers?: object): Promise<string>
发送 GET 请求

**参数**:
- `url`: 请求 URL
- `headers`: 可选的请求头

**返回**: 响应文本

**示例**:
```javascript
const html = await long.http.get('https://example.com');
```

### post(url: string, data: any, headers?: object): Promise<string>
发送 POST 请求

**参数**:
- `url`: 请求 URL
- `data`: 请求体（会自动序列化为 JSON）
- `headers`: 可选的请求头

**示例**:
```javascript
const response = await long.http.post('https://api.example.com', {
    name: 'Alice',
    age: 25
});
```

### download(url: string, savePath: string): Promise<void>
下载文件

**权限**: `network.http`

---

## 📸 screenshot - 屏幕截图

### capture(): Promise<string>
截取全屏

**返回**: Base64 编码的 PNG 图片

**示例**:
```javascript
const base64 = await long.screenshot.capture();
await long.storage.set('screenshot', base64);
```

### captureRegion(x: number, y: number, width: number, height: number): Promise<string>
截取指定区域

**权限**: `system.screenshot`

---

## 💻 shell - Shell 执行

### openUrl(url: string): Promise<void>
打开 URL（浏览器）

**示例**:
```javascript
await long.shell.openUrl('https://github.com');
```

### openFile(path: string): Promise<void>
打开文件（默认程序）

**示例**:
```javascript
await long.shell.openFile('C:\\document.pdf');
```

**权限**: `system.shell.execute`

---

## 📎 ads - NTFS 备注流

### read(filePath: string, streamName: string): Promise<string>
读取 ADS 流

**参数**:
- `filePath`: 文件路径
- `streamName`: 流名称

**示例**:
```javascript
const note = await long.ads.read('C:\\file.txt', 'note');
```

### write(filePath: string, streamName: string, content: string): Promise<void>
写入 ADS 流

**权限**: `fs.ads.access`

---

## 🪟 window - 窗口管理

### createWindow(title: string, html: string, width: number, height: number, resizable: boolean): Promise<string>
创建自定义窗口

**参数**:
- `title`: 窗口标题
- `html`: HTML 内容
- `width`: 窗口宽度
- `height`: 窗口高度
- `resizable`: 是否可调整大小

**返回**: 窗口 ID

**示例**:
```javascript
const windowId = await long.ui.createWindow(
    '我的窗口',
    '<h1>Hello!</h1>',
    400,
    300,
    true
);
```

### closeWindow(windowId: string): Promise<void>
关闭窗口

**权限**: `system.window`

---

## 🔧 process - 进程管理

### start(path: string, args?: string): Promise<void>
启动进程

**参数**:
- `path`: 可执行文件路径
- `args`: 可选的命令行参数

**示例**:
```javascript
await long.process.start('notepad.exe');
await long.process.start('cmd.exe', '/c echo Hello');
```

### getList(filter?: string): Promise<ProcessInfo[]>
获取进程列表

**参数**:
- `filter`: 可选的进程名过滤器

**返回**: 进程信息数组

**示例**:
```javascript
const processes = await long.process.getList('chrome');
processes.forEach(p => {
    console.log(`${p.name} (PID: ${p.id})`);
});
```

### kill(pid: number): Promise<void>
结束进程

**权限**: `system.process`

---

## 🔊 audio - 音频控制

### setVolume(volume: number): Promise<void>
设置音量（0-100）

**示例**:
```javascript
await long.audio.setVolume(50);
```

### getVolume(): Promise<number>
获取当前音量

### setMute(mute: boolean): Promise<void>
设置静音状态

**示例**:
```javascript
await long.audio.setMute(true); // 静音
```

### playSound(type: string): Promise<void>
播放系统声音

**参数**:
- `type`: 声音类型（'beep' | 'asterisk' | 'exclamation' | 'hand' | 'question'）

**权限**: `system.audio`

---

## 🖥️ monitor - 多显示器

### getList(): Promise<MonitorInfo[]>
获取显示器列表

**返回**: 显示器信息数组

**示例**:
```javascript
const monitors = await long.monitor.getList();
monitors.forEach(m => {
    console.log(`显示器 ${m.id}: ${m.width}x${m.height}`);
});
```

### getPrimary(): Promise<MonitorInfo>
获取主显示器

**权限**: `system.monitor`

---

## 🔋 power - 电源管理

### getBatteryStatus(): Promise<BatteryStatus>
获取电池状态

**返回**:
```typescript
{
    charging: boolean;
    level: number; // 0-100
    timeRemaining: number; // 分钟
}
```

**示例**:
```javascript
const battery = await long.power.getBatteryStatus();
if (battery.level < 20) {
    await long.notification.show('电量低于 20%', 'warning');
}
```

### getPowerPlan(): Promise<string>
获取当前电源方案

### setPowerPlan(plan: string): Promise<void>
设置电源方案

**权限**: `system.power`

---

## 🌐 network - 网络状态

### isConnected(): Promise<boolean>
检查网络连接

**示例**:
```javascript
const connected = await long.network.isConnected();
if (!connected) {
    await long.notification.show('网络未连接', 'error');
}
```

### getType(): Promise<string>
获取网络类型

**返回**: 'WiFi' | 'Ethernet' | 'None'

### getLocalIp(): Promise<string>
获取本机 IP 地址

### ping(host: string): Promise<number>
Ping 测试

**返回**: 延迟（毫秒）

**权限**: `system.network`

---

## 🌡️ sensor - 传感器数据

### getCpuTemperature(): Promise<number>
获取 CPU 温度（摄氏度）

**示例**:
```javascript
const temp = await long.sensor.getCpuTemperature();
if (temp > 80) {
    await long.notification.show(`CPU 温度过高: ${temp}°C`, 'warning');
}
```

### getGpuTemperature(): Promise<number>
获取 GPU 温度

### getFanSpeed(): Promise<number>
获取风扇转速（RPM）

**权限**: `system.sensor`

---

## 🎨 theme - 主题

### set(theme: string): Promise<void>
设置主题

**参数**:
- `theme`: 'light' | 'dark'

**示例**:
```javascript
await long.theme.set('dark');
```

**权限**: `system.theme`

---

## 错误码说明

所有 API 调用失败时会抛出错误，包含以下信息：

```typescript
{
    code: ApiErrorCode;
    message: string;
}
```

**错误码**:
- `InvalidArgument` - 参数无效
- `PermissionDenied` - 权限不足
- `NotFound` - 资源不存在
- `Unknown` - 未知错误

**错误处理示例**:
```javascript
try {
    await long.http.get('https://invalid-url');
} catch (error) {
    console.error('API 错误:', error.message);
}
```

---

**所有 API 都是异步的，需要使用 `await` 或 `.then()`** 🚀
