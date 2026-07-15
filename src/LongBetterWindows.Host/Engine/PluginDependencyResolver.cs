using System.Collections.Generic;
using System.Linq;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 插件依赖解析器 - 计算插件加载顺序
    /// </summary>
    public class PluginDependencyResolver
    {
        /// <summary>
        /// 解析插件依赖，返回正确的加载顺序
        /// </summary>
        /// <returns>排序后的插件 ID 列表，失败时返回 null</returns>
        public static List<string>? ResolveDependencies(Dictionary<string, PluginManifest> manifests)
        {
            var sorted = new List<string>();
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();

            foreach (var pluginId in manifests.Keys)
            {
                if (!TopologicalSort(pluginId, manifests, visiting, visited, sorted))
                {
                    // 检测到循环依赖
                    return null;
                }
            }

            return sorted;
        }

        private static bool TopologicalSort(
            string pluginId,
            Dictionary<string, PluginManifest> manifests,
            HashSet<string> visiting,
            HashSet<string> visited,
            List<string> sorted)
        {
            if (visited.Contains(pluginId))
                return true;

            if (visiting.Contains(pluginId))
            {
                // 循环依赖
                return false;
            }

            visiting.Add(pluginId);

            if (manifests.TryGetValue(pluginId, out var manifest))
            {
                foreach (var dep in manifest.Dependencies)
                {
                    if (!manifests.ContainsKey(dep.PluginId))
                    {
                        // 依赖缺失，跳过（调用者应该处理）
                        continue;
                    }

                    if (!TopologicalSort(dep.PluginId, manifests, visiting, visited, sorted))
                    {
                        return false;
                    }
                }
            }

            visiting.Remove(pluginId);
            visited.Add(pluginId);
            sorted.Add(pluginId);

            return true;
        }

        /// <summary>
        /// 检查依赖是否满足
        /// </summary>
        public static DependencyCheckResult CheckDependencies(
            PluginManifest manifest,
            Dictionary<string, PluginEntry> loadedPlugins)
        {
            var missing = new List<PluginDependency>();
            var versionMismatch = new List<(PluginDependency Required, string ActualVersion)>();

            foreach (var dep in manifest.Dependencies)
            {
                if (!loadedPlugins.TryGetValue(dep.PluginId, out var entry))
                {
                    missing.Add(dep);
                    continue;
                }

                // 检查版本是否满足
                if (!string.IsNullOrEmpty(dep.MinVersion))
                {
                    if (!IsVersionSatisfied(entry.Manifest.Version, dep.MinVersion))
                    {
                        versionMismatch.Add((dep, entry.Manifest.Version));
                    }
                }
            }

            return new DependencyCheckResult
            {
                IsSatisfied = missing.Count == 0 && versionMismatch.Count == 0,
                MissingDependencies = missing,
                VersionMismatches = versionMismatch,
            };
        }

        private static bool IsVersionSatisfied(string actualVersion, string minVersion)
        {
            // 简单的版本比较 (1.2.3 格式)
            var actual = ParseVersion(actualVersion);
            var min = ParseVersion(minVersion);

            if (actual.Major > min.Major) return true;
            if (actual.Major < min.Major) return false;

            if (actual.Minor > min.Minor) return true;
            if (actual.Minor < min.Minor) return false;

            return actual.Patch >= min.Patch;
        }

        private static (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            var parts = version.Split('.');
            var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
            var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
            return (major, minor, patch);
        }

        /// <summary>
        /// 查找依赖于指定插件的其他插件
        /// </summary>
        public static List<string> FindDependents(
            string pluginId,
            Dictionary<string, PluginManifest> manifests)
        {
            var dependents = new List<string>();

            foreach (var (id, manifest) in manifests)
            {
                if (manifest.Dependencies.Any(d => d.PluginId == pluginId))
                {
                    dependents.Add(id);
                }
            }

            return dependents;
        }
    }

    public class DependencyCheckResult
    {
        public bool IsSatisfied { get; init; }
        public List<PluginDependency> MissingDependencies { get; init; } = new();
        public List<(PluginDependency Required, string ActualVersion)> VersionMismatches { get; init; } = new();
    }
}
