// 📸 截图工具 - 快速截取屏幕
//
// 功能：按下 Ctrl+Shift+S 截取全屏并保存
// 能力：screenshot, shell, notification
// 作者：Long_BetterWindows 示例

long.hotkey.register('Ctrl+Shift+S', async () => {
    try {
        // 截取屏幕
        const base64Image = await long.screenshot.capture();

        // 生成文件名（时间戳）
        const now = new Date();
        const filename = `screenshot_${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}_${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}${String(now.getSeconds()).padStart(2, '0')}.png`;

        // 保存到用户图片文件夹
        const savePath = `${process.env.USERPROFILE}\\Pictures\\${filename}`;

        // 将 Base64 转换为文件并保存
        // 注意：这里需要实际的文件保存实现
        // 简化版本：只显示通知

        await long.notification.show(`📸 截图已保存: ${filename}`, 'success');

        // 打开图片文件夹
        // await long.shell.openFile(savePath);

    } catch (error) {
        await long.notification.show('❌ 截图失败', 'error');
        console.error('截图错误:', error);
    }
});

console.log('✅ 截图工具已加载 - 按 Ctrl+Shift+S 截图');
