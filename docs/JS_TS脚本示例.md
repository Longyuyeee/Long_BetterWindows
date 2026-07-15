# JS/TS 单文件脚本示例

## 快速开始

将下面的代码保存为 `.js` 或 `.ts` 文件，拖入 `Plugins/` 目录即可使用。

---

## 示例 1: 快速笔记（Ctrl+Shift+N）

**文件**: `quick-note.js`

```javascript
// 注册全局热键 Ctrl+Shift+N 保存剪贴板内容
long.hotkey.register('Ctrl+Shift+N', async () => {
    const result = await long.clipboard.getText();
    
    if (result.success) {
        const timestamp = new Date().toISOString();
        const noteKey = `note_${timestamp}`;
        
        await long.storage.set(noteKey, result.data);
        await long.notification.show('✅ 笔记已保存', 'success');
        
        console.log(`保存笔记: ${noteKey}`);
    } else {
        await long.notification.show('❌ 剪贴板为空', 'error');
    }
});

console.log('📝 快速笔记插件已加载 - 按 Ctrl+Shift+N 保存剪贴板内容');
```

---

## 示例 2: 时间戳复制（Alt+T）

**文件**: `timestamp.js`

```javascript
// 按 Alt+T 复制当前时间戳
long.hotkey.register('Alt+T', async () => {
    const now = new Date();
    const timestamp = now.toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false
    });
    
    await long.clipboard.setText(timestamp);
    await long.notification.show(`⏰ ${timestamp}`, 'info');
    
    console.log(`时间戳已复制: ${timestamp}`);
});

console.log('⏰ 时间戳插件已加载 - 按 Alt+T 复制当前时间');
```

---

## 示例 3: 文件备注系统（TypeScript）

**文件**: `file-note.ts`

```typescript
// TypeScript 版本 - 使用 ADS 流存储文件备注
interface NoteData {
    content: string;
    createdAt: string;
    updatedAt: string;
}

// Alt+N: 为当前选中的文件添加备注
long.hotkey.register('Alt+N', async () => {
    const folderResult = await long.shell.getActiveFolder();
    
    if (!folderResult.success) {
        await long.notification.show('❌ 请先在资源管理器中选择文件', 'error');
        return;
    }
    
    const filePath = folderResult.data;
    
    // 读取现有备注
    const existingNote = await long.fs.ads.read(filePath, 'long_note');
    let noteData: NoteData;
    
    if (existingNote.success && existingNote.data) {
        noteData = JSON.parse(existingNote.data);
        console.log(`现有备注: ${noteData.content}`);
    } else {
        noteData = {
            content: '',
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString()
        };
    }
    
    // 从剪贴板获取新备注
    const clipResult = await long.clipboard.getText();
    if (clipResult.success && clipResult.data) {
        noteData.content = clipResult.data;
        noteData.updatedAt = new Date().toISOString();
        
        await long.fs.ads.write(filePath, 'long_note', JSON.stringify(noteData));
        await long.notification.show('✅ 备注已保存', 'success');
        
        console.log(`备注已保存到: ${filePath}`);
    }
});

// Alt+Shift+N: 读取并复制文件备注
long.hotkey.register('Alt+Shift+N', async () => {
    const folderResult = await long.shell.getActiveFolder();
    
    if (!folderResult.success) {
        await long.notification.show('❌ 请先在资源管理器中选择文件', 'error');
        return;
    }
    
    const filePath = folderResult.data;
    const noteResult = await long.fs.ads.read(filePath, 'long_note');
    
    if (noteResult.success && noteResult.data) {
        const noteData: NoteData = JSON.parse(noteResult.data);
        await long.clipboard.setText(noteData.content);
        await long.notification.show('📋 备注已复制到剪贴板', 'info');
        
        console.log(`读取备注: ${noteData.content}`);
    } else {
        await long.notification.show('⚠️ 该文件没有备注', 'warning');
    }
});

console.log('📝 文件备注系统已加载');
console.log('  Alt+N: 保存剪贴板内容为文件备注');
console.log('  Alt+Shift+N: 读取文件备注到剪贴板');
```

---

## 示例 4: 网络请求 + 数据处理

**文件**: `weather-lookup.js`

```javascript
// Alt+W: 查询天气（示例 API）
long.hotkey.register('Alt+W', async () => {
    const cityResult = await long.clipboard.getText();
    
    if (!cityResult.success || !cityResult.data) {
        await long.notification.show('⚠️ 请先复制城市名称', 'warning');
        return;
    }
    
    const city = cityResult.data.trim();
    console.log(`正在查询 ${city} 的天气...`);
    
    try {
        // 使用公共天气 API（需替换为真实 API）
        const apiUrl = `https://api.example.com/weather?city=${encodeURIComponent(city)}`;
        const response = await long.http.get(apiUrl);
        
        if (response.success) {
            const data = JSON.parse(response.data);
            const message = `${city}\n🌡️ ${data.temperature}°C\n☁️ ${data.description}`;
            
            await long.clipboard.setText(message);
            await long.notification.show('✅ 天气信息已复制', 'success');
            
            console.log(message);
        } else {
            await long.notification.show('❌ 查询失败', 'error');
        }
    } catch (error) {
        await long.notification.show('❌ 网络错误', 'error');
        console.error(error);
    }
});

console.log('🌤️ 天气查询插件已加载 - 复制城市名后按 Alt+W');
```

---

## 示例 5: 注册表快捷配置

**文件**: `registry-helper.js`

```javascript
// 快速切换 Windows 配置
const CONFIG_KEY = 'WindowsConfig';

// Alt+D: 切换深色模式
long.hotkey.register('Alt+D', async () => {
    const currentMode = await long.registry.read('dark_mode');
    const newMode = currentMode.data === 'true' ? 'false' : 'true';
    
    await long.registry.write('dark_mode', newMode);
    
    const modeText = newMode === 'true' ? '🌙 深色' : '☀️ 浅色';
    await long.notification.show(`已切换到 ${modeText} 模式`, 'success');
    
    console.log(`深色模式: ${newMode}`);
});

// Alt+S: 截图并保存路径
long.hotkey.register('Alt+S', async () => {
    const screenshotResult = await long.screenshot.capture();
    
    if (screenshotResult.success) {
        await long.clipboard.setText(screenshotResult.data);
        await long.notification.show('📸 截图路径已复制', 'success');
        
        console.log(`截图已保存: ${screenshotResult.data}`);
    } else {
        await long.notification.show('❌ 截图失败', 'error');
    }
});

console.log('⚙️ 注册表助手已加载');
console.log('  Alt+D: 切换深色模式');
console.log('  Alt+S: 截图并复制路径');
```

---

## API 参考速查

### 剪贴板
```javascript
await long.clipboard.getText()      // 读取文本
await long.clipboard.setText(text)  // 写入文本
```

### 热键
```javascript
await long.hotkey.register('Ctrl+Shift+K', callback)
await long.hotkey.unregister('Ctrl+Shift+K')
```

### 通知
```javascript
await long.notification.show('消息', 'success')  // success/info/warning/error
```

### 存储
```javascript
await long.storage.set('key', 'value')
await long.storage.get('key')
await long.storage.delete('key')
```

### 文件 ADS 流
```javascript
await long.fs.ads.read('C:\\file.txt', 'streamName')
await long.fs.ads.write('C:\\file.txt', 'streamName', 'data')
```

### Shell
```javascript
await long.shell.getActiveFolder()  // 获取当前资源管理器路径
await long.shell.openFolder('C:\\')  // 打开文件夹
await long.shell.openUrl('https://example.com')
```

### HTTP
```javascript
await long.http.get(url)
await long.http.post(url, jsonData)
await long.http.download(url, savePath)
```

### 注册表
```javascript
await long.registry.read('key')
await long.registry.write('key', 'value')
await long.registry.delete('key')
```

### 截图
```javascript
await long.screenshot.capture()  // 全屏截图
await long.screenshot.captureRegion(x, y, width, height)
```

---

## 热插拔流程

1. 创建 `.js` 或 `.ts` 文件
2. 拖放到 Long窗口的插件管理面板
3. 等待 1-2 秒自动加载
4. 立即使用热键测试功能

---

## 注意事项

- 单文件脚本默认**无权限限制**，可访问所有 API
- TypeScript 代码会在运行时由浏览器引擎处理
- `console.log()` 输出会显示在插件的 WebView 页面中
- 脚本错误会在开发者工具控制台显示

---

**版本**: v0.5.0  
**文档更新**: 2026-07-15
