// ⚙️ 进程管理器 - 快速启动和管理进程
//
// 功能：管理常用应用程序的启动和关闭
// 能力：process, storage, notification, hotkey
// 作者：Long_BetterWindows 示例

interface AppConfig {
    name: string;
    path: string;
    hotkey?: string;
}

// 预设应用列表
const defaultApps: AppConfig[] = [
    { name: '记事本', path: 'notepad.exe', hotkey: 'Ctrl+Alt+N' },
    { name: '计算器', path: 'calc.exe', hotkey: 'Ctrl+Alt+C' },
    { name: '画图', path: 'mspaint.exe', hotkey: 'Ctrl+Alt+P' }
];

// 启动应用
async function startApp(app: AppConfig): Promise<void> {
    try {
        await long.process.start(app.path);
        await long.notification.show(`✅ 已启动: ${app.name}`, 'success');
    } catch (error) {
        await long.notification.show(`❌ 启动失败: ${app.name}`, 'error');
        console.error('启动应用失败:', error);
    }
}

// 检查应用是否运行
async function isAppRunning(processName: string): Promise<boolean> {
    try {
        const processes = await long.process.getList(processName);
        return processes.length > 0;
    } catch (error) {
        return false;
    }
}

// 结束应用
async function killApp(processName: string): Promise<void> {
    try {
        const processes = await long.process.getList(processName);
        if (processes.length === 0) {
            await long.notification.show('进程未运行', 'warning');
            return;
        }

        for (const proc of processes) {
            await long.process.kill(proc.id);
        }

        await long.notification.show(`✅ 已结束: ${processName}`, 'success');
    } catch (error) {
        await long.notification.show(`❌ 结束失败: ${processName}`, 'error');
        console.error('结束进程失败:', error);
    }
}

// 初始化：加载配置并注册热键
async function initialize(): Promise<void> {
    // 加载用户配置（如果存在）
    const savedApps = await long.storage.get('process_manager_apps');
    const apps: AppConfig[] = savedApps || defaultApps;

    // 为每个应用注册热键
    for (const app of apps) {
        if (app.hotkey) {
            long.hotkey.register(app.hotkey, async () => {
                const processName = app.path.replace('.exe', '');
                const running = await isAppRunning(processName);

                if (running) {
                    await killApp(processName);
                } else {
                    await startApp(app);
                }
            });
        }
    }

    // 保存配置
    if (!savedApps) {
        await long.storage.set('process_manager_apps', apps);
    }

    console.log('✅ 进程管理器已加载');
    console.log('   Ctrl+Alt+N - 记事本');
    console.log('   Ctrl+Alt+C - 计算器');
    console.log('   Ctrl+Alt+P - 画图');
}

// 启动插件
initialize();
