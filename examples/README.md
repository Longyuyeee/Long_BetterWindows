# Long_BetterWindows 示例插件索引

本目录包含 7 个完整的示例插件，覆盖 JavaScript、TypeScript 和 C# 三种语言。

另有可直接导入 Tool Center 的 [组合动作样例](workflows/README.md)，用于演示跨步骤结构化输出绑定。

---

## 📚 示例列表

### 1. ⏰ 时间戳工具 (timestamp)
**语言**: JavaScript  
**快捷键**: `Alt+T`  
**功能**: 快速复制当前时间到剪贴板  
**能力**: clipboard, notification

**适合学习**:
- 第一个插件入门
- 热键注册
- 剪贴板操作

[查看详情](timestamp/README.md)

---

### 2. 🔊 音量控制器 (volume)
**语言**: JavaScript  
**快捷键**: `Ctrl+Up/Down`, `Ctrl+M`  
**功能**: 快捷键调节系统音量和静音  
**能力**: audio, hotkey, notification

**适合学习**:
- 音频控制 API
- 多热键管理
- 数值范围控制

[查看详情](volume/README.md)

---

### 3. 📸 截图工具 (screenshot)
**语言**: JavaScript  
**快捷键**: `Ctrl+Shift+S`  
**功能**: 截取全屏并保存到图片文件夹  
**能力**: screenshot, shell, notification

**适合学习**:
- 屏幕截图 API
- Base64 图片处理
- 文件路径操作

[查看详情](screenshot/README.md)

---

### 4. 🌤️ 天气查询 (weather)
**语言**: TypeScript  
**快捷键**: `Ctrl+W`  
**功能**: 查询并显示本地天气信息  
**能力**: http, storage, notification

**适合学习**:
- TypeScript 类型系统
- HTTP API 调用
- 缓存机制实现
- 配置持久化

[查看详情](weather/README.md)

---

### 5. ⚙️ 进程管理器 (process-manager)
**语言**: TypeScript  
**快捷键**: `Ctrl+Alt+N/C/P`  
**功能**: 快速启动和关闭常用应用  
**能力**: process, storage, notification, hotkey

**适合学习**:
- 进程管理 API
- 配置系统设计
- 智能状态检测

[查看详情](process-manager/README.md)

---

### 6. 📝 快速笔记 (quick-note)
**语言**: C# 脚本  
**快捷键**: `Ctrl+Shift+N`  
**功能**: 弹出笔记窗口，支持多条笔记  
**能力**: storage, window, hotkey

**适合学习**:
- C# 脚本语法
- 自定义窗口创建
- HTML/CSS/JS 嵌入
- 数据持久化

[查看详情](quick-note/README.md)

---

### 7. 💻 系统信息面板 (sysinfo)
**语言**: C# 脚本  
**快捷键**: `Ctrl+I`  
**功能**: 实时监控 CPU、内存、温度  
**能力**: sensor, window, process, hotkey

**适合学习**:
- 传感器数据读取
- 实时数据更新
- 复杂界面布局
- 进程信息查询

[查看详情](sysinfo/README.md)

---

## 🎯 按能力查找

### 基础能力
- **clipboard**: timestamp
- **hotkey**: 所有示例
- **notification**: 所有示例
- **storage**: weather, process-manager, quick-note

### 系统交互
- **process**: process-manager, sysinfo
- **shell**: screenshot
- **window**: quick-note, sysinfo

### 网络与硬件
- **http**: weather
- **audio**: volume
- **screenshot**: screenshot
- **sensor**: sysinfo

---

## 📖 按语言查找

### JavaScript (3 个)
- timestamp - 时间戳工具
- volume - 音量控制器
- screenshot - 截图工具

### TypeScript (2 个)
- weather - 天气查询
- process-manager - 进程管理器

### C# 脚本 (2 个)
- quick-note - 快速笔记
- sysinfo - 系统信息面板

---

## 🚀 快速开始

### 1. 安装示例插件

**方式 A: 拖放安装**
1. 将示例文件拖入 Long_BetterWindows
2. 等待 2 秒自动加载
3. 使用快捷键测试

**方式 B: 复制到插件目录**
```bash
# 复制到插件目录
cp -r examples/timestamp Plugins/
```

### 2. 修改和学习

每个示例都包含：
- ✅ 完整的功能代码
- ✅ 详细的 README
- ✅ 能力声明说明
- ✅ 扩展建议
- ✅ 学习要点

### 3. 创建自己的插件

参考示例代码，查看 [开发者文档](../docs/DEVELOPER_GUIDE.md) 开始创建。

---

## 💡 学习路径建议

### 初学者路径
1. **timestamp** - 最简单，理解基础概念
2. **volume** - 学习多热键和状态管理
3. **screenshot** - 文件操作和 Base64 处理

### 进阶路径
4. **weather** - TypeScript + HTTP + 缓存
5. **process-manager** - 复杂配置系统
6. **quick-note** - C# + 自定义窗口
7. **sysinfo** - 综合应用，实时监控

---

## 🔧 常见问题

### Q: 示例插件不工作？
A: 检查能力声明，确保插件有正确的权限。

### Q: 如何修改热键？
A: 修改代码中的 `long.hotkey.register()` 或 `Host.HotKey.RegisterAsync()` 的第一个参数。

### Q: TypeScript 插件如何编译？
A: 拖入后自动编译，无需手动操作。

### Q: C# 脚本报错？
A: 确保使用 `Host.` 前缀访问 API，而不是 `long.`

---

## 📚 更多资源

- [用户手册](../docs/USER_GUIDE.md)
- [开发者文档](../docs/DEVELOPER_GUIDE.md)
- [API 完整参考](../docs/API_REFERENCE.md)

---

**通过这些示例，你可以学会创建自己的 Windows 效率工具！** 🎉
