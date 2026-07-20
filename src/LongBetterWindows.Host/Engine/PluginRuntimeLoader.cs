using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;

namespace LongBetterWindows.Host.Engine
{
    internal enum PluginRuntimeKind
    {
        Native,
        CSharpScript,
        WebView,
    }

    internal sealed record PluginRuntimeLoadResult(
        PluginRuntimeKind Kind,
        bool IsSuccess,
        ILongPlugin? Instance,
        PluginLoadContext? LoadContext,
        string? Error)
    {
        public static PluginRuntimeLoadResult Success(
            PluginRuntimeKind kind,
            ILongPlugin instance,
            PluginLoadContext? loadContext = null)
            => new(kind, true, instance, loadContext, null);

        public static PluginRuntimeLoadResult Failure(
            PluginRuntimeKind kind,
            string error)
            => new(kind, false, null, null, error);
    }

    internal sealed class PluginRuntimeLoader
    {
        private readonly PluginLoader _nativeLoader = new();
        private readonly ScriptPluginLoader _scriptLoader = new();

        public async Task<PluginRuntimeLoadResult> LoadAsync(
            string pluginDirectory,
            PluginManifest manifest)
        {
            var kind = GetRuntimeKind(manifest.Runtime);
            switch (kind)
            {
                case PluginRuntimeKind.CSharpScript:
                    var script = await _scriptLoader.LoadAsync(pluginDirectory, manifest);
                    return script.IsSuccess
                        ? PluginRuntimeLoadResult.Success(
                            kind,
                            new ScriptPluginAdapter(
                                script.Globals!, manifest.Id, manifest.Name, manifest.Version))
                        : PluginRuntimeLoadResult.Failure(
                            kind, script.Error ?? "脚本插件加载失败");

                case PluginRuntimeKind.WebView:
                    try
                    {
                        var runtime = new WebPluginRuntime(manifest, pluginDirectory);
                        return PluginRuntimeLoadResult.Success(
                            kind,
                            new WebPluginAdapter(
                                runtime,
                                manifest.Id,
                                manifest.Name,
                                manifest.Version,
                                pluginDirectory,
                                manifest.EntryPoint));
                    }
                    catch (Exception ex)
                    {
                        return PluginRuntimeLoadResult.Failure(
                            kind, $"Web 插件创建失败: {ex.Message}");
                    }

                default:
                    var native = await _nativeLoader.LoadAsync(pluginDirectory, manifest);
                    return native.IsSuccess
                        ? PluginRuntimeLoadResult.Success(
                            kind, native.Instance!, native.Context)
                        : PluginRuntimeLoadResult.Failure(
                            kind, native.Error ?? "原生插件加载失败");
            }
        }

        public void Release(PluginRuntimeLoadResult result, string pluginId)
        {
            switch (result.Kind)
            {
                case PluginRuntimeKind.Native when result.LoadContext is not null:
                    _nativeLoader.Unload(result.LoadContext);
                    break;
                case PluginRuntimeKind.CSharpScript:
                    _scriptLoader.Unload(pluginId);
                    break;
                case PluginRuntimeKind.WebView when result.Instance is IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        internal static PluginRuntimeKind GetRuntimeKind(string? runtime)
            => runtime?.Trim().ToLowerInvariant() switch
            {
                "csharp-script" => PluginRuntimeKind.CSharpScript,
                "webview" => PluginRuntimeKind.WebView,
                _ => PluginRuntimeKind.Native,
            };
    }
}
