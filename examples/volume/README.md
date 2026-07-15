# 音量控制器

使用快捷键快速控制系统音量。

## 功能

- `Ctrl+Up` - 增加音量（+5%）
- `Ctrl+Down` - 减少音量（-5%）
- `Ctrl+M` - 切换静音
- 显示当前音量通知
- 播放提示音反馈

## 使用方法

1. 将 `volume.js` 拖入 Long_BetterWindows
2. 等待自动加载
3. 使用快捷键控制音量

## 能力声明

- `system.audio` - 音量控制
- `system.hotkey` - 全局热键
- `system.notification` - 显示通知

## 配置

可以修改音量步进值：
```javascript
const VOLUME_STEP = 5; // 改为 10 则每次调节 10%
```

## 扩展建议

1. 添加音量预设（25%、50%、75%、100%）
2. 支持不同应用的独立音量控制
3. 添加音量曲线调节
4. 会议一键静音模式

## 学习要点

1. 使用 `long.audio.setVolume()` 设置音量
2. 使用 `long.audio.getVolume()` 获取当前音量
3. 使用 `long.audio.setMute()` 控制静音
4. 使用 `Math.min()/Math.max()` 限制数值范围
