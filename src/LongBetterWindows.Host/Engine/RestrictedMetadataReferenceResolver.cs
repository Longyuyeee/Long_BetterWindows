using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Serilog;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 受限的元数据引用解析器，防止脚本加载危险程序集
    /// </summary>
    internal class RestrictedMetadataReferenceResolver : MetadataReferenceResolver
    {
        private static readonly string[] BlockedNamespaces =
        {
            "System.IO",
            "System.Diagnostics",
            "System.Net",
            "System.Reflection",
            "System.Runtime.InteropServices",
            "System.Runtime.Loader",
            "Microsoft.Win32"
        };

        private static readonly string[] AllowedAssemblies =
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Threading.Tasks",
            "LongBetterWindows.Host"
        };

        public override bool Equals(object? other) => other is RestrictedMetadataReferenceResolver;
        public override int GetHashCode() => typeof(RestrictedMetadataReferenceResolver).GetHashCode();

        public override ImmutableArray<PortableExecutableReference> ResolveReference(
            string reference,
            string? baseFilePath,
            MetadataReferenceProperties properties)
        {
            // 阻止加载危险的程序集
            if (BlockedNamespaces.Any(ns => reference.StartsWith(ns, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("脚本插件尝试引用被阻止的命名空间: {Reference}", reference);
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            // 只允许白名单中的程序集
            if (!AllowedAssemblies.Any(allowed =>
                reference.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("脚本插件尝试引用未授权的程序集: {Reference}", reference);
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            // 使用默认解析器处理允许的引用
            try
            {
                var assembly = Assembly.Load(reference);
                if (assembly != null)
                {
                    return ImmutableArray.Create(
                        MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "无法加载程序集: {Reference}", reference);
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }
    }
}
