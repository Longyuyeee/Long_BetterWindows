using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    public static class ManifestReader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        public static readonly HashSet<string> KnownCapabilities = new(StringComparer.OrdinalIgnoreCase)
        {
            "system.hotkey",
            "shell.context_menu",
            "shell.selection",
            "shell.ui.locator",
            "ui.floating_box",
            "fs.ads.access",
            "system.registry.read",
            "system.registry.write",
            "system.clipboard",
            "file.ops",
            "window.info",
            "system.screenshot",
            "system.input",
            "system.process",
            "storage.local",
        };

        public static async Task<ManifestResult> ReadAsync(string pluginDir)
        {
            var manifestPath = Path.Combine(pluginDir, "manifest.json");

            if (!File.Exists(manifestPath))
            {
                return ManifestResult.Fail("manifest.json 未找到。");
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(manifestPath);
            }
            catch (Exception ex)
            {
                return ManifestResult.Fail($"无法读取 manifest.json: {ex.Message}");
            }

            PluginManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options)
                    ?? new PluginManifest();
            }
            catch (JsonException ex)
            {
                return ManifestResult.Fail($"manifest.json JSON 解析失败: {ex.Message}");
            }

            return Validate(manifest);
        }

        private static ManifestResult Validate(PluginManifest manifest)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(manifest.Id))
                errors.Add("缺少必填字段: id");
            if (string.IsNullOrWhiteSpace(manifest.Version))
                errors.Add("缺少必填字段: version");
            if (string.IsNullOrWhiteSpace(manifest.Name))
                errors.Add("缺少必填字段: name");
            if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
                errors.Add("缺少必填字段: entry_point");

            if (!IsValidVersion(manifest.Version))
                errors.Add($"版本号格式无效: '{manifest.Version}'（期望: x.y.z）");

            foreach (var cap in manifest.Capabilities)
            {
                if (!KnownCapabilities.Contains(cap))
                    errors.Add($"未知能力声明: '{cap}'");
            }

            // ApiVersion 兼容性检查
            if (!string.IsNullOrWhiteSpace(manifest.MinApiVersion))
            {
                if (TryParseVersion(manifest.MinApiVersion, out var reqMajor, out var reqMinor, out _))
                {
                    var requested = new Contracts.ApiVersion(reqMajor, reqMinor, 0);
                    if (!Contracts.ApiVersion.Current.IsCompatibleWith(requested))
                        errors.Add($"API 版本不兼容: 插件要求 {requested}, 当前 {Contracts.ApiVersion.Current}");
                }
            }

            if (errors.Count > 0)
                return ManifestResult.Fail(string.Join("; ", errors));

            return ManifestResult.Ok(manifest);
        }

        private static bool IsValidVersion(string version)
        {
            return TryParseVersion(version, out _, out _, out _);
        }

        private static bool TryParseVersion(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrWhiteSpace(version)) return false;
            var parts = version.Split('.');
            if (parts.Length < 2 || parts.Length > 3) return false;
            return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor)
                && (parts.Length == 2 || int.TryParse(parts[2], out patch));
        }
    }

    public class ManifestResult
    {
        public bool IsSuccess { get; init; }
        public PluginManifest? Manifest { get; init; }
        public string? Error { get; init; }

        public static ManifestResult Ok(PluginManifest manifest)
            => new() { IsSuccess = true, Manifest = manifest };

        public static ManifestResult Fail(string error)
            => new() { IsSuccess = false, Error = error };
    }
}
