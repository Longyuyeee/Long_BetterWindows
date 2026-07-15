// 🔊 音量控制器 - 快捷键控制音量
//
// 功能：Ctrl+Up/Down 调节音量，Ctrl+M 静音
// 能力：audio, hotkey, notification
// 作者：Long_BetterWindows 示例

// 音量步进值
const VOLUME_STEP = 5;

// 增加音量
long.hotkey.register('Ctrl+Up', async () => {
    try {
        const currentVolume = await long.audio.getVolume();
        const newVolume = Math.min(100, currentVolume + VOLUME_STEP);

        await long.audio.setVolume(newVolume);
        await long.notification.show(`🔊 音量: ${newVolume}%`, 'info');

        // 播放提示音
        await long.audio.playSound('beep');

    } catch (error) {
        console.error('增加音量失败:', error);
    }
});

// 减少音量
long.hotkey.register('Ctrl+Down', async () => {
    try {
        const currentVolume = await long.audio.getVolume();
        const newVolume = Math.max(0, currentVolume - VOLUME_STEP);

        await long.audio.setVolume(newVolume);
        await long.notification.show(`🔉 音量: ${newVolume}%`, 'info');

        // 播放提示音
        await long.audio.playSound('beep');

    } catch (error) {
        console.error('减少音量失败:', error);
    }
});

// 切换静音
long.hotkey.register('Ctrl+M', async () => {
    try {
        const isMuted = await long.audio.getMute();
        await long.audio.setMute(!isMuted);

        if (!isMuted) {
            await long.notification.show('🔇 已静音', 'warning');
        } else {
            await long.notification.show('🔊 已取消静音', 'success');
            await long.audio.playSound('beep');
        }

    } catch (error) {
        console.error('切换静音失败:', error);
    }
});

console.log('✅ 音量控制器已加载');
console.log('   Ctrl+Up   - 增加音量');
console.log('   Ctrl+Down - 减少音量');
console.log('   Ctrl+M    - 切换静音');
