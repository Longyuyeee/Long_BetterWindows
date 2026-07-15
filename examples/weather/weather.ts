// 🌤️ 天气查询 - 显示本地天气信息
//
// 功能：Ctrl+W 查询并显示天气
// 能力：http, storage, notification
// 作者：Long_BetterWindows 示例

interface WeatherData {
    city: string;
    temperature: number;
    condition: string;
    humidity: number;
    lastUpdate: number;
}

// 缓存时间（30分钟）
const CACHE_DURATION = 30 * 60 * 1000;

async function getWeather(): Promise<WeatherData | null> {
    try {
        // 检查缓存
        const cached = await long.storage.get('weather_cache');
        if (cached && Date.now() - cached.lastUpdate < CACHE_DURATION) {
            return cached as WeatherData;
        }

        // 获取城市（示例使用北京）
        const city = (await long.storage.get('weather_city')) || '北京';

        // 调用天气 API（示例 URL，需要替换为真实 API）
        const apiUrl = `https://api.example.com/weather?city=${encodeURIComponent(city)}`;
        const response = await long.http.get(apiUrl);
        const data = JSON.parse(response);

        // 解析天气数据
        const weather: WeatherData = {
            city: data.city || city,
            temperature: data.temp || 20,
            condition: data.condition || '晴',
            humidity: data.humidity || 60,
            lastUpdate: Date.now()
        };

        // 缓存结果
        await long.storage.set('weather_cache', weather);

        return weather;

    } catch (error) {
        console.error('获取天气失败:', error);
        return null;
    }
}

// 注册热键
long.hotkey.register('Ctrl+W', async () => {
    await long.notification.show('🌤️ 正在查询天气...', 'info');

    const weather = await getWeather();

    if (weather) {
        const message = `${weather.city}\n🌡️ ${weather.temperature}°C ${weather.condition}\n💧 湿度 ${weather.humidity}%`;
        await long.notification.show(message, 'success');
    } else {
        await long.notification.show('❌ 天气查询失败', 'error');
    }
});

console.log('✅ 天气查询已加载 - 按 Ctrl+W 查询天气');
