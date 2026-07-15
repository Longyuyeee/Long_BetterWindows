// ⏰ 时间戳工具 - 快速复制当前时间
//
// 功能：按下 Alt+T 复制当前时间到剪贴板
// 能力：clipboard, notification
// 作者：Long_BetterWindows 示例

long.hotkey.register('Alt+T', async () => {
    const now = new Date();

    // 格式化时间
    const time = now.toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false
    });

    // 复制到剪贴板
    await long.clipboard.setText(time);

    // 显示通知
    await long.notification.show(`⏰ 已复制: ${time}`, 'success');
});

console.log('✅ 时间戳工具已加载 - 按 Alt+T 复制当前时间');
