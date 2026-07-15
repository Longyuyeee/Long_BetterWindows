# 系统信息面板

实时监控系统状态，包括 CPU 温度、内存使用、进程信息。

## 功能

- `Ctrl+I` - 打开系统信息窗口
- 实时显示 CPU/GPU 温度
- 显示内存使用情况
- 显示进程数量
- TOP 5 资源占用进程

## 使用方法

1. 将 `sysinfo.csx` 拖入 Long_BetterWindows
2. 等待自动加载
3. 按 `Ctrl+I` 打开系统信息窗口

## 能力声明

- `system.sensor` - 获取硬件传感器数据
- `system.window` - 创建自定义窗口
- `system.process` - 获取进程信息
- `system.hotkey` - 全局热键

## 特性

- 深色主题界面
- 实时数据更新（1秒刷新）
- 网格布局
- 资源占用排行

## 扩展建议

1. 添加 CPU 使用率曲线图
2. 添加网络流量监控
3. 支持硬盘读写速度
4. 添加系统警报阈值

## 学习要点

1. 使用 `Host.Sensor` 获取传感器数据
2. 使用 `Host.Process` 获取进程信息
3. 在窗口中实现数据实时更新
4. CSS Grid 布局
5. JavaScript setInterval 定时器
