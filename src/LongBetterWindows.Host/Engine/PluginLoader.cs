using System.IO;
using System.Reflection;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Core;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    public class PluginLoader
    {
        private readonly List<PluginLoadContext> _contexts = new();

        public Task<PluginLoadResult> LoadAsync(string pluginDir, PluginManifest manifest)
        {
            return Task.Run(() => LoadInternal(pluginDir, manifest));
        }

        private PluginLoadResult LoadInternal(string pluginDir, PluginManifest manifest)
        {
            var assemblyPath = Path.Combine(pluginDir, manifest.EntryPoint);

            if (!File.Exists(assemblyPath))
            {
                return PluginLoadResult.Fail($"入口程序集未找到: {assemblyPath}");
            }

            try
            {
                var context = new PluginLoadContext(pluginDir);
                _contexts.Add(context);

                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var pluginType = FindPluginType(assembly);

                if (pluginType == null)
                {
                    context.Unload();
                    _contexts.Remove(context);
                    return PluginLoadResult.Fail("未找到实现 ILongPlugin 的类型。");
                }

                if (Activator.CreateInstance(pluginType) is not ILongPlugin instance)
                {
                    context.Unload();
                    _contexts.Remove(context);
                    return PluginLoadResult.Fail("无法实例化插件类型。");
                }

                Log.Information("插件 {PluginId} 加载成功，类型: {PluginType}",
                    manifest.Id, pluginType.FullName);

                return PluginLoadResult.Ok(instance, context);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "插件 {PluginId} 加载失败", manifest.Id);
                return PluginLoadResult.Fail($"插件加载异常: {ex.Message}");
            }
        }

        public void Unload(PluginLoadContext context)
        {
            _contexts.Remove(context);
            context.Unload();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Log.Information("插件上下文已卸载");
        }

        private static Type? FindPluginType(Assembly assembly)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (typeof(ILongPlugin).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            return null;
        }
    }

    public class PluginLoadResult
    {
        public bool IsSuccess { get; init; }
        public ILongPlugin? Instance { get; init; }
        public PluginLoadContext? Context { get; init; }
        public string? Error { get; init; }

        public static PluginLoadResult Ok(ILongPlugin instance, PluginLoadContext context)
            => new() { IsSuccess = true, Instance = instance, Context = context };

        public static PluginLoadResult Fail(string error)
            => new() { IsSuccess = false, Error = error };
    }
}
