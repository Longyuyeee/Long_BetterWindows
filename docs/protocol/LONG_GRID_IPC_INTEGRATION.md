# Long Grid：LPWP 1.0 IPC 最小接入说明

## 1. 依赖与边界

Long Grid 只依赖 `LongBetterWindows.PluginIpc` 1.1.0，不引用
`LongBetterWindows.Host`、WPF 或任何插件 DLL。Long助手必须已启动，双方进程必须处于
同一 Windows 用户、同一登录会话和同一完整性级别；管理员与非管理员进程不能互相代理。

NuGet 内的 `fixtures/ipc/*.json` 是跨仓库 Golden Fixtures。升级 IPC 包时应先在 Long Grid
测试中反序列化这些文件，阻止字段名、错误码和结果形状发生静默漂移。

## 2. 最小连接流程

```csharp
await using var client = new LongPluginBrokerClient();
var hello = await client.ConnectAsync(new HostHelloRequest(
    "Long Grid",
    gridVersion,
    [IpcProtocol.Name],
    [BrokerMethods.PluginCatalogList, BrokerMethods.CommandInvoke,
     BrokerMethods.CommandCancel, BrokerMethods.PluginOpen]));

var catalog = await client.RequestAsync<
    PluginCatalogListRequest,
    PluginCatalogListResponse>(
        BrokerMethods.PluginCatalogList,
        new PluginCatalogListRequest());
```

首帧必须是 `host.hello`。只有 `hello.Features` 中存在的方法才可启用；连接失败时 Long Grid
显示离线态，不直接扫描 Long助手插件目录。

## 3. 命令与取消

需要取消的命令应由调用方生成 GUID，并在同一连接发送取消：

```csharp
var requestId = Guid.NewGuid().ToString();
var running = client.RequestWithIdAsync<CommandInvokeRequest, CommandInvokeResponse>(
    requestId,
    BrokerMethods.CommandInvoke,
    new CommandInvokeRequest(pluginId, commandId, arguments),
    deadlineMilliseconds: 10_000);

await client.CancelCommandAsync(requestId);
```

同一插件最多 4 个并发命令。Long Grid 只在错误的 `retryable` 为 `true` 时指数退避重试；
`capability_denied`、`invalid_request` 和 `surface_not_supported` 不自动重试。

## 4. 可执行探针

仓库提供无 Host/WPF 依赖的 `long-ipc-probe`：

```powershell
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --ping
dotnet run --project tools/LongBetterWindows.PluginIpcProbe
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --get <plugin-id>
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --invoke <plugin-id> <command-id>
dotnet run --project tools/LongBetterWindows.PluginIpcProbe -- --open <plugin-id>
```

无参数时执行握手并输出脱敏目录。退出码 `0` 表示成功，`2` 表示稳定 Broker 错误，`3`
表示宿主离线或连接失败。Long Grid 接入验收至少复现 ping、catalog、一个无副作用命令、
显式取消、宿主退出后的离线态和宿主重启后的重新连接。
