using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// C# 脚本插件加载器——写代码即生效，无需编译。
    ///
    /// manifest.json 中指定 "runtime": "csharp-script" 时使用此加载器。
    /// 脚本入口为 .csx 文件，通过 Roslyn CSharpScript 直接执行。
    /// 热重载：修改 .csx 文件后自动重新执行。
    /// </summary>
    public class ScriptPluginLoader
    {
        private readonly Dictionary<string, ScriptState<object>> _states = new();

        public async Task<ScriptLoadResult> LoadAsync(
            string pluginDir, PluginManifest manifest)
        {
            var scriptPath = Path.Combine(pluginDir, manifest.EntryPoint);
            if (!File.Exists(scriptPath))
            {
                return ScriptLoadResult.Fail($"脚本文件不存在: {scriptPath}");
            }

            try
            {
                var code = await File.ReadAllTextAsync(scriptPath);
                var host = HostProvider.Instance;

                // ✅ 创建受限的脚本选项
                // 只引用宿主程序集，不允许加载其他程序集
                var options = ScriptOptions.Default
                    .WithReferences(
                        typeof(IHostApi).Assembly,
                        typeof(PluginState).Assembly,
                        Assembly.Load("System.Runtime"))
                    .WithImports(
                        // 只导入安全的命名空间，避免导入危险类型
                        "System.Threading.Tasks",
                        "LongBetterWindows.Host.Core",
                        "LongBetterWindows.Host.Capabilities",
                        "LongBetterWindows.Host.Contracts")
                    // 注意：不导入 System.IO, System.Diagnostics, System.Net, System.Reflection
                    // 脚本必须通过 Host API 访问系统功能
                    .WithMetadataResolver(new RestrictedMetadataReferenceResolver());

                var script = CSharpScript.Create(code, options,
                    globalsType: typeof(ScriptGlobals));

                var globals = new ScriptGlobals
                {
                    Host = host,
                    Id = manifest.Id,
                    Name = manifest.Name,
                };

                var state = await script.RunAsync(globals);
                _states[manifest.Id] = state;

                Log.Information("脚本插件 {PluginId} 执行完成", manifest.Id);
                return ScriptLoadResult.Ok(globals);
            }
            catch (CompilationErrorException ex)
            {
                var errors = string.Join("\n", ex.Diagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                Log.Error("脚本插件 {PluginId} 编译错误:\n{Errors}", manifest.Id, errors);
                return ScriptLoadResult.Fail($"编译错误: {errors}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "脚本插件 {PluginId} 执行失败", manifest.Id);
                return ScriptLoadResult.Fail(ex.Message);
            }
        }

        public void Unload(string pluginId)
        {
            _states.Remove(pluginId);
        }
    }

    /// <summary>
    /// 传递给脚本的全局变量。
    /// 脚本中可直接使用 Host, Id, Name 变量。
    /// </summary>
    public class ScriptGlobals
    {
        /// <summary>宿主 API，脚本通过 Host.HotKey, Host.Clipboard 等访问能力</summary>
        public IHostApi Host { get; init; } = null!;

        /// <summary>插件 ID</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>插件名称</summary>
        public string Name { get; init; } = string.Empty;

        // 脚本可以定义这些委托来响应生命周期
        // 用法: Start = async () => { await Host.HotKey.RegisterAsync(...); };
        public Func<Task>? Start { get; set; }
        public Func<Task>? Stop { get; set; }
        public Func<PluginLanguageContext, Task>? LanguageChanged { get; set; }
    }

    public class ScriptLoadResult
    {
        public bool IsSuccess { get; init; }
        public ScriptGlobals? Globals { get; init; }
        public string? Error { get; init; }

        public static ScriptLoadResult Ok(ScriptGlobals globals)
            => new() { IsSuccess = true, Globals = globals };

        public static ScriptLoadResult Fail(string error)
            => new() { IsSuccess = false, Error = error };
    }
}
