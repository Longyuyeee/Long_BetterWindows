# UI 窗口 API 使用示例

## 概述

Long_BetterWindows v0.5.0 新增了 `long.ui` 窗口 API，允许插件创建自定义 UI 窗口。

---

## API 列表

### 1. `long.ui.createWindow(title, htmlContent, width, height, resizable)`

创建一个新的 UI 窗口。

**参数**:
- `title` (string) - 窗口标题
- `htmlContent` (string) - HTML 内容（完整的 HTML 文档）
- `width` (number, 可选) - 窗口宽度，默认 600px
- `height` (number, 可选) - 窗口高度，默认 400px
- `resizable` (boolean, 可选) - 是否可调整大小，默认 true

**返回**: `Promise<{ success: boolean, data?: string, error?: string }>`
- `data` - 窗口 ID（用于后续操作）

**示例**:
```javascript
const result = await long.ui.createWindow(
    '我的窗口',
    `<!DOCTYPE html>
    <html>
    <head>
        <meta charset="UTF-8">
        <style>
            body { 
                font-family: Arial; 
                background: #1e1f22; 
                color: #fff; 
                padding: 20px; 
            }
        </style>
    </head>
    <body>
        <h1>欢迎使用自定义窗口</h1>
        <p>这是插件创建的窗口</p>
    </body>
    </html>`,
    800,
    600,
    true
);

if (result.success) {
    console.log('窗口已创建，ID:', result.data);
}
```

---

### 2. `long.ui.closeWindow(windowId)`

关闭指定的窗口。

**参数**:
- `windowId` (string) - 窗口 ID

**返回**: `Promise<{ success: boolean, error?: string }>`

**示例**:
```javascript
await long.ui.closeWindow('window_abc123');
```

---

### 3. `long.ui.sendMessage(windowId, message)`

向窗口发送消息（触发窗口内的 `hostMessage` 事件）。

**参数**:
- `windowId` (string) - 窗口 ID
- `message` (string) - JSON 格式的消息内容

**返回**: `Promise<{ success: boolean, error?: string }>`

**示例**:
```javascript
// 发送消息
await long.ui.sendMessage(windowId, JSON.stringify({
    action: 'update',
    data: { count: 42 }
}));
```

**窗口内接收消息**:
```javascript
// 在窗口的 HTML 中监听
window.addEventListener('hostMessage', (event) => {
    const message = event.detail;
    console.log('收到宿主消息:', message);
});
```

---

## 完整示例

### 示例 1: 倒计时窗口

```javascript
// countdown-window.js
// 需要在 manifest.json 中声明: "ui.window"

let windowId = null;

long.hotkey.register('Ctrl+Shift+C', async () => {
    const result = await long.ui.createWindow(
        '番茄钟',
        `<!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <style>
                body {
                    display: flex;
                    justify-content: center;
                    align-items: center;
                    height: 100vh;
                    margin: 0;
                    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                    font-family: 'Segoe UI', Arial, sans-serif;
                }
                .timer {
                    text-align: center;
                    color: white;
                }
                .time {
                    font-size: 72px;
                    font-weight: bold;
                    margin: 20px 0;
                }
                button {
                    background: rgba(255,255,255,0.2);
                    border: 2px solid white;
                    color: white;
                    padding: 12px 32px;
                    font-size: 16px;
                    border-radius: 8px;
                    cursor: pointer;
                    margin: 0 8px;
                }
                button:hover {
                    background: rgba(255,255,255,0.3);
                }
            </style>
        </head>
        <body>
            <div class="timer">
                <h1>番茄钟</h1>
                <div class="time" id="time">25:00</div>
                <button onclick="start()">开始</button>
                <button onclick="reset()">重置</button>
            </div>
            <script>
                let seconds = 25 * 60;
                let timer = null;
                
                function start() {
                    if (timer) return;
                    timer = setInterval(() => {
                        seconds--;
                        updateDisplay();
                        if (seconds <= 0) {
                            clearInterval(timer);
                            timer = null;
                            alert('时间到！');
                        }
                    }, 1000);
                }
                
                function reset() {
                    if (timer) clearInterval(timer);
                    timer = null;
                    seconds = 25 * 60;
                    updateDisplay();
                }
                
                function updateDisplay() {
                    const m = Math.floor(seconds / 60);
                    const s = seconds % 60;
                    document.getElementById('time').textContent = 
                        \`\${m}:\${s.toString().padStart(2, '0')}\`;
                }
            </script>
        </body>
        </html>`,
        400,
        300,
        false
    );
    
    if (result.success) {
        windowId = result.data;
        await long.notification.show('番茄钟已启动', 'success');
    }
});

console.log('⏰ 番茄钟插件已加载（Ctrl+Shift+C 打开）');
```

---

### 示例 2: 实时数据监控

```javascript
// monitor-window.js
// 需要声明: "ui.window", "network.http"

let monitorWindowId = null;
let updateInterval = null;

long.hotkey.register('Ctrl+Shift+M', async () => {
    const result = await long.ui.createWindow(
        '系统监控',
        `<!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <style>
                body {
                    margin: 0;
                    padding: 20px;
                    background: #0f172a;
                    color: #f8fafc;
                    font-family: 'Consolas', monospace;
                }
                .metric {
                    background: #1e293b;
                    padding: 16px;
                    margin: 12px 0;
                    border-radius: 8px;
                    border-left: 4px solid #38bdf8;
                }
                .label {
                    font-size: 12px;
                    color: #94a3b8;
                    text-transform: uppercase;
                }
                .value {
                    font-size: 32px;
                    font-weight: bold;
                    margin-top: 8px;
                }
            </style>
        </head>
        <body>
            <h2>📊 系统监控</h2>
            <div class="metric">
                <div class="label">CPU 使用率</div>
                <div class="value" id="cpu">--</div>
            </div>
            <div class="metric">
                <div class="label">内存使用</div>
                <div class="value" id="memory">--</div>
            </div>
            <div class="metric">
                <div class="label">活动窗口</div>
                <div class="value" id="window">--</div>
            </div>
            <script>
                window.addEventListener('hostMessage', (event) => {
                    const data = JSON.parse(event.detail);
                    document.getElementById('cpu').textContent = data.cpu;
                    document.getElementById('memory').textContent = data.memory;
                    document.getElementById('window').textContent = data.window;
                });
            </script>
        </body>
        </html>`,
        500,
        450,
        true
    );
    
    if (result.success) {
        monitorWindowId = result.data;
        
        // 每秒更新一次数据
        updateInterval = setInterval(async () => {
            const windowInfo = await long.window.getForeground();
            
            const data = {
                cpu: Math.floor(Math.random() * 100) + '%',
                memory: Math.floor(Math.random() * 16) + ' GB',
                window: windowInfo.title || '未知窗口'
            };
            
            await long.ui.sendMessage(
                monitorWindowId, 
                JSON.stringify(data)
            );
        }, 1000);
    }
});

// 清理
window.addEventListener('beforeunload', () => {
    if (updateInterval) clearInterval(updateInterval);
});

console.log('📊 监控插件已加载（Ctrl+Shift+M 打开）');
```

---

### 示例 3: 笔记编辑器

```javascript
// note-editor.js
// 需要声明: "ui.window", "fs.ads.access"

let editorWindowId = null;

long.hotkey.register('Ctrl+Shift+N', async () => {
    // 获取当前选中的文件
    const items = await long.shell.getSelectedItems();
    if (!items.success || items.data.length === 0) {
        await long.notification.show('请先选择一个文件', 'error');
        return;
    }
    
    const filePath = items.data[0];
    
    // 读取现有笔记
    const existing = await long.fs.ads.read(filePath, 'long_note');
    const currentNote = existing.success ? existing.data : '';
    
    const result = await long.ui.createWindow(
        `笔记编辑器 - ${filePath.split('\\').pop()}`,
        `<!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <style>
                body {
                    margin: 0;
                    padding: 16px;
                    background: #1e1f22;
                    color: #fff;
                    font-family: Arial;
                }
                textarea {
                    width: 100%;
                    height: calc(100vh - 120px);
                    background: #2b2d30;
                    color: #fff;
                    border: 1px solid #3c3f41;
                    border-radius: 4px;
                    padding: 12px;
                    font-size: 14px;
                    resize: none;
                }
                .toolbar {
                    margin-top: 12px;
                    display: flex;
                    gap: 8px;
                }
                button {
                    background: #3574f0;
                    color: white;
                    border: none;
                    padding: 8px 16px;
                    border-radius: 4px;
                    cursor: pointer;
                }
                button:hover { background: #2563eb; }
                .cancel { background: #6b7280; }
                .cancel:hover { background: #4b5563; }
            </style>
        </head>
        <body>
            <textarea id="note">${currentNote.replace(/</g, '&lt;')}</textarea>
            <div class="toolbar">
                <button onclick="save()">💾 保存</button>
                <button class="cancel" onclick="window.close()">❌ 取消</button>
            </div>
            <script>
                const filePath = '${filePath.replace(/\\/g, '\\\\')}';
                
                async function save() {
                    const content = document.getElementById('note').value;
                    // 通过 chrome.webview 回调主进程
                    window.chrome.webview.postMessage(JSON.stringify({
                        action: 'save',
                        path: filePath,
                        content: content
                    }));
                }
            </script>
        </body>
        </html>`,
        600,
        500,
        true
    );
    
    if (result.success) {
        editorWindowId = result.data;
    }
});

console.log('📝 笔记编辑器插件已加载（Ctrl+Shift+N）');
```

---

## 权限声明

使用 UI 窗口 API 需要在 `manifest.json` 中声明：

```json
{
  "id": "my-plugin",
  "name": "我的插件",
  "capabilities": [
    "ui.window"
  ]
}
```

---

## 注意事项

1. **窗口生命周期**: 创建的窗口独立于插件主页面，关闭插件不会自动关闭窗口
2. **通信机制**: 
   - 主进程 → 窗口: `long.ui.sendMessage()` 触发窗口内的 `hostMessage` 事件
   - 窗口 → 主进程: 使用 `window.chrome.webview.postMessage()` (需在主插件监听)
3. **样式建议**: 窗口内的 HTML 应该是完整的文档，包含 `<head>` 和 CSS
4. **性能**: 每个窗口都是独立的 WebView2 实例，避免创建过多窗口
5. **错误处理**: 始终检查 `result.success` 确保窗口创建成功

---

## 调试技巧

在窗口的 HTML 中添加：

```javascript
window.addEventListener('error', (e) => {
    console.error('窗口错误:', e.message);
});
```

在主插件中监听窗口消息：

```javascript
// WebView2 的 message 事件会传递到主插件
```
