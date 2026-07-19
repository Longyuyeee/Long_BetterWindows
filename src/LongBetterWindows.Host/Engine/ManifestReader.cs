using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        };

        public static readonly HashSet<string> KnownCapabilities = new(StringComparer.OrdinalIgnoreCase)
        {
            "system.hotkey",
            "shell.context_menu",
            "shell.selection",
            "shell.execute",
            "shell.ui.locator",
            "ui.floating_box",
            "fs.ads.access",
            "system.registry.read",
            "system.registry.write",
            "system.clipboard",
            "system.notification",
            "system.screenshot",
            "system.input",
            "system.process",
            "file.ops",
            "window.info",
            "storage.local",
            "network.http",
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

            ValidateCommands(manifest, errors);
            ValidateWindowPreference(manifest.Window, errors);

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

        private static void ValidateCommands(PluginManifest manifest, List<string> errors)
        {
            var commandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in manifest.Commands)
            {
                if (string.IsNullOrWhiteSpace(command.Id))
                    errors.Add("commands 中存在缺少 id 的指令");
                else if (!commandIds.Add(command.Id))
                    errors.Add($"commands 中存在重复 id: '{command.Id}'");

                if (string.IsNullOrWhiteSpace(command.Title))
                    errors.Add($"指令 '{command.Id}' 缺少 title");

                if (command.Priority is < -100 or > 100)
                    errors.Add($"指令 '{command.Id}' 的 priority 必须在 -100 到 100 之间");

                if (command.AcceptedInputs.Count == 0)
                    errors.Add($"指令 '{command.Id}' 必须声明至少一种 accepted_inputs");
            }
        }

        private static void ValidateWindowPreference(
            PluginWindowPreference? window,
            List<string> errors)
        {
            if (window == null) return;

            ValidateDimension("preferred_width", window.PreferredWidth, errors);
            ValidateDimension("preferred_height", window.PreferredHeight, errors);
            ValidateDimension("min_width", window.MinWidth, errors);
            ValidateDimension("min_height", window.MinHeight, errors);

            if (window.PreferredWidth.HasValue && window.MinWidth.HasValue
                && window.PreferredWidth < window.MinWidth)
                errors.Add("window.preferred_width 不能小于 min_width");

            if (window.PreferredHeight.HasValue && window.MinHeight.HasValue
                && window.PreferredHeight < window.MinHeight)
                errors.Add("window.preferred_height 不能小于 min_height");
        }

        private static void ValidateDimension(string name, int? value, List<string> errors)
        {
            if (value.HasValue && value.Value <= 0)
                errors.Add($"window.{name} 必须大于 0");
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
