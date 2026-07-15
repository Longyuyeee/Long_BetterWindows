# 时间戳工具

快速复制当前时间到剪贴板的简单插件。

## 功能

- 按 `Alt+T` 快速复制当前时间
- 支持中文日期时间格式
- 显示友好的成功通知

## 使用方法

1. 将 `timestamp.js` 拖入 Long_BetterWindows
2. 等待 2 秒自动加载
3. 按 `Alt+T` 测试

## 能力声明

- `system.clipboard` - 复制文本到剪贴板
- `system.notification` - 显示通知提示

## 示例输出

```
2024-05-15 14:30:45
```

## 扩展建议

可以扩展为多种时间格式：
- ISO 8601: `2024-05-15T14:30:45Z`
- Unix 时间戳: `1715772645`
- 相对时间: `2小时前`

## 学习要点

1. 使用 `long.hotkey.register()` 注册全局热键
2. 使用 `Date` 对象格式化时间
3. 使用 `long.clipboard.setText()` 复制文本
4. 使用 `long.notification.show()` 显示通知
