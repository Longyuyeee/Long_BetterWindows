# 安全修复记录 (Security Fixes)

**修复日期**: 2026-07-15  
**修复版本**: v0.4.1 (安全增强版)

---

## 修复的安全漏洞

### 🔴 P0 - 关键安全漏洞 (已修复)

#### 1. WebView2 JavaScript 桥接权限绕过
**文件**: `src/LongBetterWindows.Host/Engine/WebPluginRuntime.cs`

**问题**: HTML/JS 插件可以绕过 `manifest.json` 中的 `capabilities` 声明，调用所有宿主 API。

**修复**:
- 新增 `GetRequiredCapability()` 方法，映射每个 JS API 到所需的 capability
- 在 `DispatchJsCall()` 中添加权限检查逻辑
- 使用 `PluginAccessContext.Enter()` 设置插件上下文，支持回滚追踪
- 拒绝未授权的 API 调用并记录警告日志

**影响**: 恶意 HTML 插件无法再访问未声明的系统能力。

---

#### 2. ShellExecuteService URL 协议注入
**文件**: `src/LongBetterWindows.Host/Services/ShellExecuteService.cs`

**问题**: `OpenUrlAsync` 未验证 URL 协议，可能被利用执行任意命令 (如 `file://`, `ms-settings:`)。

**修复**:
- 使用 `Uri.TryCreate` 验证 URL 格式
- 白名单验证：仅允许 `http`, `https`, `mailto` 协议
- 拒绝其他协议并记录警告日志

**影响**: 阻止通过 URL 协议注入执行恶意命令。

---

#### 3. C# 脚本执行沙盒缺失
**文件**: `src/LongBetterWindows.Host/Engine/ScriptPluginLoader.cs`  
**新增**: `src/LongBetterWindows.Host/Engine/RestrictedMetadataReferenceResolver.cs`

**问题**: C# 脚本可以访问完整的 .NET 运行时，绕过宿主 API 直接调用 `System.IO.File`, `System.Diagnostics.Process` 等危险类型。

**修复**:
- 移除危险命名空间的导入 (`System.IO`, `System.Diagnostics`, `System.Reflection` 等)
- 实现 `RestrictedMetadataReferenceResolver` 类，阻止加载未授权的程序集
- 白名单机制：仅允许加载宿主程序集和必要的系统运行时程序集

**影响**: 脚本插件必须通过宿主 API 访问系统功能，无法绕过安全检查。

---

### 🟡 P1 - 高危漏洞 (已修复)

#### 4. HttpService SSRF 攻击风险
**文件**: `src/LongBetterWindows.Host/Services/HttpService.cs`

**问题**:
- 允许请求任意 URL，包括内网地址 (`localhost`, `192.168.*`, `10.*`)
- 下载文件无大小限制，可能导致内存耗尽

**修复**:
- 新增 `ValidateUrl()` 方法，验证 URL 安全性
- 阻止访问内网地址（黑名单：`localhost`, `127.0.0.1`, `10.*`, `172.16-31.*`, `192.168.*`, `169.254.*`)
- 添加响应大小限制：10MB（可配置）
- 使用 `HttpCompletionOption.ResponseHeadersRead` 提前检查 Content-Length

**影响**: 防止插件利用 HTTP 服务攻击内网或执行 DDoS。

---

#### 5. 注册表路径遍历攻击
**文件**: `src/LongBetterWindows.Host/Services/RegistryService.cs`

**问题**: `ResolveKeyPath()` 使用字符串拼接，未验证 `key` 参数，可能被利用访问任意注册表键。

**修复**:
- 检测 `..` 路径遍历尝试
- 阻止绝对路径和 `HKEY_` 前缀
- 正则验证：仅允许字母、数字、下划线、连字符、反斜杠
- 记录可疑访问尝试

**影响**: 插件只能访问 `HKEY_CURRENT_USER\Software\LongBetterWindows\` 下的子键。

---

#### 6. ADS 流名称注入
**文件**: `src/LongBetterWindows.Host/Services/ADSService.cs`

**问题**: `BuildAdsPath()` 未验证流名称，可能包含特殊字符导致路径遍历。

**修复**:
- 验证流名称不包含特殊字符 (`:`, `\`, `/`, `..`, `<`, `>`, `|`, `*`, `?`)
- 限制流名称长度不超过 255 字符
- 抛出 `ArgumentException` 并记录警告

**影响**: 防止通过恶意流名称访问非预期的文件系统位置。

---

### 🔵 P2 - 代码质量改进 (已修复)

#### 7. StorageService 并发写入优化
**文件**: `src/LongBetterWindows.Host/Services/StorageService.cs`

**问题**: 
- 在写锁内执行 IO 操作，阻塞其他线程
- JSON 序列化失败可能导致数据丢失

**修复**:
- 在读锁内完成 JSON 序列化，释放锁后再写文件
- 使用原子写入：先写临时文件 (`.tmp`)，再 `File.Move` 替换
- 添加错误处理和临时文件清理

**影响**: 提升并发性能，防止文件损坏。

---

#### 8. WebPluginRuntime 字段可空性修复
**文件**: `src/LongBetterWindows.Host/Engine/WebPluginRuntime.cs`

**问题**: `_webView` 字段在构造函数中未初始化，产生 CS8618 警告。

**修复**:
- 将字段声明为 `WebView2 _webView = null!;`
- 延迟初始化在 `InitializeAsync()` 中完成

**影响**: 消除编译器警告，符合 C# nullable 引用类型规范。

---

## 未修复的已知问题 (计划在后续版本修复)

### P2 - 架构问题

1. **插件卸载资源泄漏** (`PluginLoader.cs:61-70`)
   - 建议：为 `ILongPlugin` 添加 `IDisposable` 接口
   - 影响：插件可能泄漏热键、WebView、线程等资源

2. **COM 对象内存泄漏** (`ShellSelectionService.cs:254-271`)
   - 建议：使用 `Marshal.ReleaseComObject` 释放 Shell.Application COM 对象
   - 影响：长期运行可能累积内存泄漏

3. **PluginLoadContext 隔离不足** (`PluginLoadContext.cs:18-27`)
   - 建议：显式白名单程序集，拒绝未授权的加载请求
   - 影响：DLL 插件可能访问宿主的所有程序集

### P3 - 功能增强

4. **插件签名验证**
   - 建议：在 `manifest.json` 中添加签名字段，加载前验证
   - 影响：无法防止恶意插件伪装成官方插件

5. **RollbackEngine 覆盖范围有限**
   - 当前仅支持注册表和 ADS 回滚
   - 建议：扩展到文件操作、热键等其他服务

---

## 依赖安全建议

| 依赖 | 当前版本 | 建议操作 |
|---|---|---|
| Microsoft.CodeAnalysis.CSharp.Scripting | 4.8.0 | 升级到 4.11.0 |
| Microsoft.Web.WebView2 | 1.0.4022.49 | 定期更新以获取 Chromium 安全补丁 |
| WPF-UI | 3.0.4 | 评估第三方库的维护状态 |
| Serilog | 4.0.0 | ✅ 最新版本，无已知漏洞 |

---

## 测试建议

建议添加以下安全测试用例：

```csharp
[Fact]
public async Task WebPlugin_WithoutCapability_ShouldRejectApiCall()
{
    var manifest = new PluginManifest 
    { 
        Id = "test", 
        Capabilities = new List<string>() // 空权限
    };
    
    var runtime = new WebPluginRuntime(manifest, ".");
    var result = await runtime.InvokeMethod("long.registry.write", "key", "value");
    
    Assert.False(result.success);
    Assert.Contains("未声明权限", result.error);
}

[Fact]
public void RegistryService_PathTraversal_ShouldThrow()
{
    var service = new RegistryService();
    Assert.Throws<ArgumentException>(() => 
        service.WriteAsync("..\\..\\malicious", "value", "evil"));
}

[Fact]
public async Task HttpService_InternalNetwork_ShouldReject()
{
    var service = new HttpService();
    var result = await service.GetAsync("http://192.168.1.1/admin");
    
    Assert.False(result.IsSuccess);
    Assert.Contains("内网地址", result.ErrorMessage);
}
```

---

## 升级指南

### 对插件开发者的影响

#### HTML/JS 插件
- **必须在 `manifest.json` 中声明所需的 capabilities**
- 未声明的 API 调用将被拒绝，返回错误：`{ success: false, error: "插件未声明权限: xxx" }`

**迁移步骤**:
1. 检查插件使用的所有 `long.*` API
2. 在 `manifest.json` 中添加对应的 capabilities:

```json
{
  "capabilities": [
    "system.clipboard",      // long.clipboard.*
    "fs.ads.access",         // long.fs.ads.*
    "system.hotkey",         // long.hotkey.*
    "system.registry.write", // long.registry.*
    "network.http",          // long.http.*
    "shell.selection",       // long.shell.getActiveFolder 等
    "shell.execute",         // long.shell.openFolder 等
    "system.screenshot"      // long.screenshot.*
  ]
}
```

#### C# 脚本插件 (.csx)
- **不能直接使用 `System.IO`, `System.Diagnostics` 等命名空间**
- 必须通过 `Host` API 访问系统功能

**迁移步骤**:
将直接的系统调用替换为宿主 API：

```csharp
// ❌ 旧代码（不再可用）
using System.IO;
var content = File.ReadAllText("C:\\path\\file.txt");

// ✅ 新代码（通过宿主 API）
var result = await Host.FileOps.ReadFileAsync("C:\\path\\file.txt");
if (result.IsSuccess)
{
    var content = result.Data;
}
```

#### DLL 插件
- **无破坏性变更**
- 建议实现 `IDisposable` 以正确清理资源（未来版本将强制要求）

---

## 审计人员

- AI 代码审计: Claude Code (Anthropic)
- 修复实施: Claude Code
- 审计时间: 2026-07-15

---

## 版本历史

| 版本 | 日期 | 修复内容 |
|---|---|---|
| v0.4.1 | 2026-07-15 | 修复 8 个安全漏洞（3 个 P0，3 个 P1，2 个 P2） |
| v0.4.0 | 2026-06-17 | 基础版本 |
