# 进程管理器

快捷键快速启动和关闭常用应用程序。

## 功能

- `Ctrl+Alt+N` - 启动/关闭记事本
- `Ctrl+Alt+C` - 启动/关闭计算器
- `Ctrl+Alt+P` - 启动/关闭画图
- 智能检测：运行中则关闭，未运行则启动

## 使用方法

1. 将 `process-manager.ts` 拖入 Long_BetterWindows
2. 等待自动编译和加载
3. 使用快捷键启动应用

## 能力声明

- `system.process` - 进程管理
- `system.storage` - 数据存储
- `system.notification` - 显示通知
- `system.hotkey` - 全局热键

## 自定义应用

在控制台执行：
```typescript
const apps = [
    { name: 'VSCode', path: 'code.exe', hotkey: 'Ctrl+Alt+V' },
    { name: 'Chrome', path: 'chrome.exe', hotkey: 'Ctrl+Alt+B' }
];
await long.storage.set('process_manager_apps', apps);
```

然后重新加载插件。

## 扩展建议

1. 添加应用图标显示
2. 支持启动参数配置
3. 添加进程优先级设置
4. 支持批量启动预设

## 学习要点

1. 使用 `long.process.start()` 启动进程
2. 使用 `long.process.getList()` 查询进程
3. 使用 `long.process.kill()` 结束进程
4. TypeScript 接口和类型安全
5. 配置持久化
