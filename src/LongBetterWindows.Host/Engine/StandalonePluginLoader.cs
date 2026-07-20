using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    internal enum StandalonePluginKind
    {
        CSharpScript,
        JavaScript,
        TypeScript,
    }

    internal sealed record StandalonePluginHandle(
        string SourcePath,
        StandalonePluginKind Kind,
        PluginManifest Manifest,
        PluginEntry Entry,
        string? TemporaryDirectory);

    internal sealed record StandalonePluginLoadResult(
        bool IsSuccess,
        StandalonePluginHandle? Handle,
        string? Error)
    {
        public static StandalonePluginLoadResult Success(StandalonePluginHandle handle)
            => new(true, handle, null);

        public static StandalonePluginLoadResult Failure(string error)
            => new(false, null, error);
    }

    internal sealed class StandalonePluginLoader
    {
        private readonly PluginRegistry _registry;
        private readonly ScriptPluginLoader _scriptLoader = new();

        public StandalonePluginLoader(PluginRegistry registry)
        {
            _registry = registry;
        }

        public async Task<StandalonePluginLoadResult> LoadAsync(string scriptPath)
        {
            var sourcePath = Path.GetFullPath(scriptPath);
            var kind = GetKind(sourcePath);
            if (kind is null)
                return StandalonePluginLoadResult.Failure("不支持的单文件脚本类型");

            if (!File.Exists(sourcePath))
                return StandalonePluginLoadResult.Failure("单文件脚本不存在");

            var scriptDirectory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(scriptDirectory))
                return StandalonePluginLoadResult.Failure("无法确定单文件脚本目录");

            var scriptContent = await File.ReadAllTextAsync(sourcePath);
            var manifest = CreateManifest(sourcePath, scriptContent, kind.Value);
            if (_registry.Get(manifest.Id) is not null)
                return StandalonePluginLoadResult.Failure($"插件 {manifest.Id} 已注册");

            return kind == StandalonePluginKind.CSharpScript
                ? await LoadCSharpAsync(sourcePath, scriptDirectory, manifest)
                : await LoadWebScriptAsync(
                    sourcePath, scriptDirectory, scriptContent, manifest, kind.Value);
        }

        public async Task UnloadAsync(StandalonePluginHandle handle)
        {
            try
            {
                if (handle.Entry.Instance is ILongPlugin plugin)
                {
                    using (PluginAccessContext.Enter(handle.Manifest.Id))
                        await plugin.StopAsync();
                }
                else if (handle.Entry.Instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "单文件插件 {PluginId} 停止时出错", handle.Manifest.Id);
            }

            _registry.Unregister(handle.Manifest.Id);
            if (handle.Kind == StandalonePluginKind.CSharpScript)
                _scriptLoader.Unload(handle.Manifest.Id);
            DeleteTemporaryDirectory(handle.TemporaryDirectory);
        }

        internal static List<string> ExtractCapabilities(string scriptContent)
        {
            var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(
                scriptContent,
                @"^\s*//\s*@capabilit(?:y|ies)\s*[:=]?\s*(.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                foreach (var value in match.Groups[1].Value.Split(
                    new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ManifestReader.KnownCapabilities.Contains(value))
                        capabilities.Add(value);
                    else
                        Log.Warning("单文件脚本声明了未知能力: {Capability}", value);
                }
            }

            return capabilities.OrderBy(value => value).ToList();
        }

        internal static string BuildJavaScriptWrapper(
            string pluginName,
            string scriptContent,
            bool isTypeScript)
        {
            var encodedSource = Convert.ToBase64String(Encoding.UTF8.GetBytes(scriptContent));
            var safeName = WebUtility.HtmlEncode(pluginName);
            var compilerTag = isTypeScript
                ? "<script src=\"typescript.js\"></script>"
                : string.Empty;
            var executeScript = isTypeScript
                ? @"
        const result = ts.transpileModule(source, {
            compilerOptions: {
                target: ts.ScriptTarget.ES2020,
                module: ts.ModuleKind.None,
                strict: false
            },
            reportDiagnostics: true
        });
        const errors = (result.diagnostics || []).filter(d => d.category === ts.DiagnosticCategory.Error);
        if (errors.length) {
            throw new Error(errors.map(d => ts.flattenDiagnosticMessageText(d.messageText, '\n')).join('\n'));
        }
        (0, eval)(result.outputText);"
                : "        (0, eval)(source);";

            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{safeName}</title>
    <style>
        body {{
            margin: 0;
            padding: 16px;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: #1a1a1d;
            color: #f8fafc;
        }}
        .script-info {{
            padding: 12px;
            background: #2a2a2d;
            border-radius: 6px;
            border-left: 3px solid #38bdf8;
            margin-bottom: 16px;
        }}
        .script-info h3 {{
            margin: 0 0 8px 0;
            font-size: 14px;
            color: #38bdf8;
        }}
        .script-info p {{
            margin: 0;
            font-size: 12px;
            color: #999;
        }}
        #output {{
            white-space: pre-wrap;
            font-family: 'Consolas', monospace;
            font-size: 12px;
        }}
    </style>
</head>
<body>
    <div class=""script-info"">
        <h3>{safeName} {(isTypeScript ? "(TypeScript)" : "(JavaScript)")}</h3>
        <p>单文件脚本 · 保存即热重载 · 能力由 @capabilities 声明</p>
    </div>
    <div id=""output""></div>

    <script>
        const output = document.getElementById('output');
        const originalLog = console.log;
        const postHostLog = (...args) => window.chrome?.webview?.postMessage({{
            id: 0,
            method: 'app.log',
            args: args.map(value => String(value))
        }});
        console.log = function(...args) {{
            originalLog.apply(console, args);
            output.textContent += args.join(' ') + '\n';
            postHostLog(...args);
        }};
        const originalError = console.error;
        console.error = function(...args) {{
            originalError.apply(console, args);
            postHostLog('[error]', ...args);
        }};
    </script>

    {compilerTag}
    <script>
    try {{
        const bytes = Uint8Array.from(atob('{encodedSource}'), c => c.charCodeAt(0));
        const source = new TextDecoder('utf-8').decode(bytes);
{executeScript}
    }} catch (error) {{
        console.error(error);
        output.textContent += '编译或执行失败: ' + (error?.message || error) + '\n';
    }}
    </script>
</body>
</html>";
        }

        private async Task<StandalonePluginLoadResult> LoadCSharpAsync(
            string sourcePath,
            string scriptDirectory,
            PluginManifest manifest)
        {
            var script = await _scriptLoader.LoadAsync(scriptDirectory, manifest);
            if (!script.IsSuccess)
                return StandalonePluginLoadResult.Failure(
                    script.Error ?? "单文件 C# 脚本加载失败");

            var plugin = new ScriptPluginAdapter(
                script.Globals!, manifest.Id, manifest.Name, manifest.Version);
            if (!_registry.Register(manifest, plugin, null, scriptDirectory))
            {
                _scriptLoader.Unload(manifest.Id);
                return StandalonePluginLoadResult.Failure($"插件 {manifest.Id} 注册失败");
            }

            try
            {
                using (PluginAccessContext.Enter(manifest.Id))
                {
                    if (!await plugin.InitializeAsync(HostProvider.Instance))
                        return await FailCSharpAsync(plugin, manifest, "初始化失败");
                    if (!await plugin.StartAsync())
                        return await FailCSharpAsync(plugin, manifest, "启动失败");
                }

                _registry.SetState(manifest.Id, PluginState.Running);
                var entry = _registry.Get(manifest.Id)!;
                return StandalonePluginLoadResult.Success(new StandalonePluginHandle(
                    sourcePath,
                    StandalonePluginKind.CSharpScript,
                    manifest,
                    entry,
                    TemporaryDirectory: null));
            }
            catch (Exception ex)
            {
                await CleanupCSharpAsync(plugin, manifest.Id);
                return StandalonePluginLoadResult.Failure(ex.Message);
            }
        }

        private async Task<StandalonePluginLoadResult> LoadWebScriptAsync(
            string sourcePath,
            string scriptDirectory,
            string scriptContent,
            PluginManifest manifest,
            StandalonePluginKind kind)
        {
            var temporaryDirectory = Path.Combine(
                scriptDirectory, $".long_temp_{Path.GetFileNameWithoutExtension(sourcePath)}");
            WebPluginRuntime? plugin = null;
            try
            {
                DeleteTemporaryDirectory(temporaryDirectory);
                Directory.CreateDirectory(temporaryDirectory);

                if (kind == StandalonePluginKind.TypeScript)
                {
                    var compilerPath = Path.Combine(
                        AppContext.BaseDirectory, "Assets", "typescript.js");
                    if (!File.Exists(compilerPath))
                        throw new FileNotFoundException(
                            "TypeScript 编译器资源缺失", compilerPath);
                    File.Copy(
                        compilerPath,
                        Path.Combine(temporaryDirectory, "typescript.js"),
                        overwrite: true);
                }

                await File.WriteAllTextAsync(
                    Path.Combine(temporaryDirectory, "index.html"),
                    BuildJavaScriptWrapper(
                        manifest.Name,
                        scriptContent,
                        kind == StandalonePluginKind.TypeScript));
                File.Copy(
                    sourcePath,
                    Path.Combine(temporaryDirectory, Path.GetFileName(sourcePath)),
                    overwrite: true);

                plugin = new WebPluginRuntime(manifest, temporaryDirectory);
                if (!_registry.Register(manifest, plugin, null, temporaryDirectory))
                {
                    plugin.Dispose();
                    DeleteTemporaryDirectory(temporaryDirectory);
                    return StandalonePluginLoadResult.Failure(
                        $"插件 {manifest.Id} 注册失败");
                }

                using (PluginAccessContext.Enter(manifest.Id))
                {
                    if (!await plugin.InitializeAsync())
                    {
                        _registry.Unregister(manifest.Id);
                        plugin.Dispose();
                        DeleteTemporaryDirectory(temporaryDirectory);
                        return StandalonePluginLoadResult.Failure("初始化失败");
                    }
                }

                _registry.SetState(manifest.Id, PluginState.Running);
                var entry = _registry.Get(manifest.Id)!;
                return StandalonePluginLoadResult.Success(new StandalonePluginHandle(
                    sourcePath, kind, manifest, entry, temporaryDirectory));
            }
            catch (Exception ex)
            {
                _registry.Unregister(manifest.Id);
                plugin?.Dispose();
                DeleteTemporaryDirectory(temporaryDirectory);
                return StandalonePluginLoadResult.Failure(ex.Message);
            }
        }

        private async Task<StandalonePluginLoadResult> FailCSharpAsync(
            ILongPlugin plugin,
            PluginManifest manifest,
            string error)
        {
            await CleanupCSharpAsync(plugin, manifest.Id);
            return StandalonePluginLoadResult.Failure(error);
        }

        private async Task CleanupCSharpAsync(ILongPlugin plugin, string pluginId)
        {
            try
            {
                await plugin.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "单文件 C# 插件 {PluginId} 清理未完全完成", pluginId);
            }

            _registry.Unregister(pluginId);
            _scriptLoader.Unload(pluginId);
        }

        private static PluginManifest CreateManifest(
            string sourcePath,
            string scriptContent,
            StandalonePluginKind kind)
        {
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var csharp = kind == StandalonePluginKind.CSharpScript;
            return new PluginManifest
            {
                Id = $"{(csharp ? "script" : "js")}-{fileName}",
                Name = fileName,
                Version = "1.0.0",
                Runtime = csharp ? "csharp-script" : "html",
                EntryPoint = csharp ? Path.GetFileName(sourcePath) : "index.html",
                Capabilities = ExtractCapabilities(scriptContent),
                Author = "User",
            };
        }

        private static StandalonePluginKind? GetKind(string sourcePath)
            => Path.GetExtension(sourcePath).ToLowerInvariant() switch
            {
                ".csx" => StandalonePluginKind.CSharpScript,
                ".js" => StandalonePluginKind.JavaScript,
                ".ts" => StandalonePluginKind.TypeScript,
                _ => null,
            };

        private static void DeleteTemporaryDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "单文件插件临时目录清理失败: {Directory}", directory);
            }
        }
    }
}
