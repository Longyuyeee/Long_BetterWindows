# 截图工具

快速截取屏幕并保存到图片文件夹。

## 功能

- 按 `Ctrl+Shift+S` 截取全屏
- 自动生成时间戳文件名
- 保存到用户的 Pictures 文件夹
- 显示成功通知

## 使用方法

1. 将 `screenshot.js` 拖入 Long_BetterWindows
2. 等待自动加载
3. 按 `Ctrl+Shift+S` 截图

## 能力声明

- `system.screenshot` - 截取屏幕
- `system.shell.execute` - 打开文件
- `system.notification` - 显示通知

## 文件命名格式

```
screenshot_20240515_143045.png
```

## 扩展建议

1. 添加区域截图功能
2. 支持多种图片格式（JPG, BMP）
3. 自动上传到云存储
4. 添加图片编辑功能

## 学习要点

1. 使用 `long.screenshot.capture()` 截取屏幕
2. 处理 Base64 图片数据
3. 生成时间戳文件名
4. 使用环境变量获取用户目录
