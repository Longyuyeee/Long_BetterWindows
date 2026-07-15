# Long_BetterWindows 用户手册

欢迎使用 Long_BetterWindows！这是一个热插拔的原子化插件平台，让你可以用 C#、JavaScript 或 TypeScript 快速创建 Windows 效率工具。

---

## 🚀 快速开始

### 安装应用
1. 下载最新版本的 Long_BetterWindows
2. 解压到任意目录
3. 运行 `LongBetterWindows.Host.exe`
4. 首次启动会自动创建配置目录

### 第一个插件
创建一个文件 `timestamp.js`，内容如下：

```javascript
// 注册热键 Alt+T
long.hotkey.register('Alt+T', async () => {
    const time = new Date().toLocaleString('zh-CN');
    await long.clipboard.setText(time);
    await long.notification.show(`已复制时间：${time}`, 'success');
});

console.log('⏰ 时间戳插件已加载');
```

将文件拖入应用窗口的插件管理区域，等待 2 秒自动加载。按下 `Alt+T` 测试！

---

## 📦 安装插件

### 方式 1: 拖放安装（推荐）
1. 打开「插件管理」面板
2. 将 `.csx`、`.js`、`.ts` 或 `.lpak` 文件拖入窗口
3. 等待自动安装和加载

### 方式 2: 从插件市场安装
1. 打开「插件市场」面板
2. 浏览或搜索插件
3. 点击「安装」按钮
4. 等待下载和自动加载

### 方式 3: 手动安装
1. 在 `Plugins/` 目录下创建插件文件夹
2. 添加 `manifest.json` 和插件文件
3. 应用会自动检测并加载

---

## 🔧 管理插件

### 启用/禁用插件
- 在插件卡片上找到开关按钮
- 点击切换插件状态
- 禁用的插件不会运行，但不会被删除

### 查看插件权限
1. 点击插件卡片上的 🔒 按钮
2. 查看插件使用的所有能力
3. 查看 API 调用统计

### 卸载插件
1. 右键点击插件卡片
2. 选择「卸载」
3. 确认删除

### 更新插件
- 应用启动时自动检查更新
- 有更新时显示横幅提示
- 点击「更新全部」一键更新

---

## 🎨 界面使用

### 主题切换
1. 点击右上角主题按钮
2. 在浅色/深色主题之间切换
3. 主题偏好会自动保存

### 响应式布局
- 应用支持 800px - 1920px 窗口宽度
- 插件市场网格会自动调整列数
- 小屏幕下优化信息密度

### 性能监控
1. 打开「性能监控」面板
2. 查看实时 CPU/内存使用
3. 查看 API 调用统计
4. 查看插件性能排行

---

## 🔑 热键系统

### 注册热键
插件可以注册全局热键：

```javascript
// JavaScript
long.hotkey.register('Ctrl+Shift+N', async () => {
    await long.notification.show('热键触发！', 'info');
});
```

```csharp
// C#
await Host.HotKey.RegisterAsync("Ctrl+Shift+N", async () => {
    await Host.Notification.ShowAsync("热键触发！", "info");
});
```

### 支持的修饰键
- `Ctrl` - Ctrl 键
- `Shift` - Shift 键
- `Alt` - Alt 键
- `Win` - Windows 键

### 支持的按键
- 字母：A-Z
- 数字：0-9
- 功能键：F1-F12
- 特殊键：Space、Tab、Enter、Esc

---

## 🔌 能力 API

插件可以使用 19 种原子能力：

### 基础能力
- **clipboard** - 剪贴板操作
- **hotkey** - 全局热键
- **storage** - 数据存储
- **notification** - 通知提示

### 系统交互
- **registry** - 注册表读写
- **process** - 进程管理
- **shell** - Shell 执行
- **window** - 窗口管理

### 网络与文件
- **http** - HTTP 请求
- **ads** - NTFS 备注流
- **screenshot** - 屏幕截图

### 硬件与电源
- **audio** - 音频控制
- **monitor** - 多显示器
- **power** - 电源管理
- **sensor** - 传感器数据
- **network** - 网络状态

### UI 与主题
- **theme** - 主题切换
- **ui** - UI 窗口创建

---

## ❓ 常见问题

### Q: 插件不加载怎么办？
1. 检查文件扩展名是否正确（.csx / .js / .ts / .lpak）
2. 查看开发工具面板的日志输出
3. 确认插件没有语法错误

### Q: 热键不生效？
1. 检查热键是否被其他程序占用
2. 确认插件已启用
3. 尝试重新注册热键

### Q: 如何调试插件？
1. 使用 `console.log()` 输出日志
2. 打开「开发工具」面板查看日志
3. 修改插件文件后自动热重载

### Q: 性能监控显示 CPU 占用高？
1. 检查哪个插件占用最高
2. 优化插件代码（减少定时器、异步操作）
3. 禁用不常用的插件

### Q: 插件市场无法连接？
1. 检查网络连接
2. 尝试刷新插件市场
3. 使用拖放安装 .lpak 文件

---

## 🛠️ 故障排除

### 应用无法启动
- 检查是否安装了 .NET 8.0 运行时
- 查看 `logs/` 目录下的日志文件
- 尝试删除 `config/` 目录重置配置

### 插件加载失败
- 查看 `logs/app.log` 中的错误信息
- 确认 manifest.json 格式正确
- 检查插件文件编码是否为 UTF-8

### 权限错误
- 某些能力需要管理员权限
- 右键「以管理员身份运行」应用
- 检查 Windows 安全中心设置

---

## 📚 进一步学习

- [开发者文档](DEVELOPER_GUIDE.md) - 如何开发插件
- [API 参考](API_REFERENCE.md) - 完整 API 文档
- [示例插件](../examples/) - 学习最佳实践

---

**享受 Long_BetterWindows 带来的效率提升！** 🎉
