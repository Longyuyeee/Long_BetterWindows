# 天气查询

查询并显示本地天气信息，支持缓存。

## 功能

- `Ctrl+W` - 查询天气
- 显示温度、天气状况、湿度
- 30分钟缓存机制
- 可配置城市

## 使用方法

1. 将 `weather.ts` 拖入 Long_BetterWindows
2. 等待自动编译和加载
3. 按 `Ctrl+W` 查询天气

## 能力声明

- `network.http` - HTTP 请求
- `system.storage` - 数据存储
- `system.notification` - 显示通知

## 配置城市

在控制台执行：
```javascript
await long.storage.set('weather_city', '上海');
```

## API 配置

需要替换为真实的天气 API：
```typescript
const apiUrl = `https://api.example.com/weather?city=${city}`;
```

推荐的免费天气 API：
- 和风天气：https://dev.qweather.com/
- OpenWeatherMap：https://openweathermap.org/api

## 扩展建议

1. 添加未来 7 天天气预报
2. 支持多城市切换
3. 添加天气图标显示
4. 空气质量指数（AQI）

## 学习要点

1. TypeScript 接口定义
2. 使用 `long.http.get()` 调用 API
3. JSON 数据解析
4. 缓存策略实现
5. 使用 `long.storage` 持久化配置
