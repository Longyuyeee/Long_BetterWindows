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
            "system.clipboard.monitor",
            "system.notification",
            "system.screenshot",
            "system.input",
            "system.process",
            "file.ops",
            "window.info",
            "storage.local",
            "network.http",
            "network.ports",
            "network.monitor",
            "system.performance",
            "filesystem.advanced",
            "text.pinyin",
            "system.cache",
            "system.schedule",
            "system.audio",
            "system.power",
            "system.theme",
            "system.wallpaper",
            "display.brightness",
            "ui.window",
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

                if (command.Outputs.Count > 64)
                    errors.Add($"指令 '{command.Id}' 不能声明超过 64 个 outputs");
                var outputKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var output in command.Outputs)
                {
                    if (!IsIdentifier(output.Key))
                        errors.Add($"指令 '{command.Id}' 的 output key 无效: '{output.Key}'");
                    else if (!outputKeys.Add(output.Key))
                        errors.Add($"指令 '{command.Id}' 存在重复 output key: '{output.Key}'");
                    if (!Enum.IsDefined(output.Type))
                        errors.Add($"指令 '{command.Id}' 的 output type 无效: '{output.Type}'");
                }

                var schemaErrorCount = errors.Count;
                ValidateArgumentSchema(command, errors);
                var schemaIsStructurallyValid = errors.Count == schemaErrorCount;
                if (schemaIsStructurallyValid)
                {
                    var defaultResult =
                        PluginCommandArgumentValidator.ValidateDeclaredDefaults(command.ArgumentSchema);
                    foreach (var issue in defaultResult.Issues)
                        errors.Add($"指令 '{command.Id}' 的参数默认值无效: {issue}");
                }

                var argumentPresets = command.ArgumentPresets
                    ?? new List<PluginCommandArgumentPreset>();
                if (argumentPresets.Count > 32)
                    errors.Add($"指令 '{command.Id}' 不能声明超过 32 个 argument_presets");
                var presetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var preset in argumentPresets)
                {
                    if (preset is null)
                    {
                        errors.Add($"指令 '{command.Id}' 包含空参数预设");
                        continue;
                    }
                    var arguments = preset.Arguments
                        ?? new Dictionary<string, string>();
                    if (!IsIdentifier(preset.Id))
                        errors.Add($"指令 '{command.Id}' 的参数预设 id 无效: '{preset.Id}'");
                    else if (!presetIds.Add(preset.Id))
                        errors.Add($"指令 '{command.Id}' 存在重复参数预设 id: '{preset.Id}'");
                    if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 120)
                        errors.Add($"指令 '{command.Id}' 的参数预设名称无效: '{preset.Name}'");
                    if (arguments.Count > 64)
                        errors.Add($"指令 '{command.Id}' 的参数预设 '{preset.Id}' 不能包含超过 64 个参数");
                    if (arguments.Any(argument =>
                        string.IsNullOrWhiteSpace(argument.Key)
                        || argument.Key.Length > 128
                        || argument.Value is null
                        || argument.Value.Length > 65536))
                    {
                        errors.Add($"指令 '{command.Id}' 的参数预设 '{preset.Id}' 包含无效参数");
                    }
                    if (arguments.Values.Sum(value => (long)(value?.Length ?? 0)) > 65536)
                        errors.Add($"指令 '{command.Id}' 的参数预设 '{preset.Id}' 参数总长度超过限制");
                    if (schemaIsStructurallyValid
                        && (command.ArgumentSchema?.Count ?? 0) > 0)
                    {
                        var presetResult = PluginCommandArgumentValidator.Validate(
                            command.ArgumentSchema,
                            arguments);
                        foreach (var issue in presetResult.Issues)
                        {
                            errors.Add(
                                $"指令 '{command.Id}' 的参数预设 '{preset.Id}' 无效: {issue}");
                        }
                    }
                }
            }
        }

        private static void ValidateArgumentSchema(PluginCommand command, List<string> errors)
        {
            const int maximumTextLength = 65536;
            var declarations = command.ArgumentSchema
                ?? new List<PluginCommandArgumentDeclaration>();
            if (declarations.Count > 64)
                errors.Add($"指令 '{command.Id}' 不能声明超过 64 个 argument_schema 参数");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var declaration in declarations)
            {
                if (declaration is null)
                {
                    errors.Add($"指令 '{command.Id}' 的 argument_schema 包含空参数声明");
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(declaration.Key)
                    ? "<empty>"
                    : declaration.Key;
                if (!IsIdentifier(declaration.Key))
                    errors.Add($"指令 '{command.Id}' 的参数 key 无效: '{declaration.Key}'");
                else if (!keys.Add(declaration.Key))
                    errors.Add($"指令 '{command.Id}' 存在重复参数 key: '{declaration.Key}'");

                if (string.IsNullOrWhiteSpace(declaration.Name) || declaration.Name.Length > 120)
                    errors.Add($"指令 '{command.Id}' 的参数 '{label}' 显示名称无效");
                if ((declaration.Description?.Length ?? 0) > 1000)
                    errors.Add($"指令 '{command.Id}' 的参数 '{label}' 说明超过 1000 个字符");
                if (!Enum.IsDefined(declaration.Type))
                    errors.Add($"指令 '{command.Id}' 的参数 '{label}' 类型无效");
                if ((declaration.DefaultValue?.Length ?? 0) > maximumTextLength)
                    errors.Add($"指令 '{command.Id}' 的参数 '{label}' 默认值超过限制");

                ValidateArgumentNumericConstraints(command.Id, label, declaration, errors);
                ValidateArgumentLengthConstraints(command.Id, label, declaration, errors);
                ValidateArgumentEnumValues(command.Id, label, declaration, errors);
            }
        }

        private static void ValidateArgumentNumericConstraints(
            string commandId,
            string label,
            PluginCommandArgumentDeclaration declaration,
            List<string> errors)
        {
            var hasNumericConstraint = declaration.Minimum.HasValue || declaration.Maximum.HasValue;
            var isNumeric = declaration.Type is PluginCommandArgumentType.Integer
                or PluginCommandArgumentType.Number;
            if (hasNumericConstraint && !isNumeric)
            {
                errors.Add($"指令 '{commandId}' 的参数 '{label}' 仅数值类型可声明 minimum/maximum");
                return;
            }

            if (declaration.Minimum > declaration.Maximum)
                errors.Add($"指令 '{commandId}' 的参数 '{label}' minimum 不能大于 maximum");

            if (declaration.Type == PluginCommandArgumentType.Integer
                && ((declaration.Minimum.HasValue
                        && decimal.Truncate(declaration.Minimum.Value) != declaration.Minimum.Value)
                    || (declaration.Maximum.HasValue
                        && decimal.Truncate(declaration.Maximum.Value) != declaration.Maximum.Value)))
            {
                errors.Add($"指令 '{commandId}' 的整数参数 '{label}' 范围必须使用整数");
            }
        }

        private static void ValidateArgumentLengthConstraints(
            string commandId,
            string label,
            PluginCommandArgumentDeclaration declaration,
            List<string> errors)
        {
            const int maximumTextLength = 65536;
            var hasLengthConstraint = declaration.MinLength.HasValue || declaration.MaxLength.HasValue;
            if (hasLengthConstraint && declaration.Type != PluginCommandArgumentType.String)
            {
                errors.Add($"指令 '{commandId}' 的参数 '{label}' 仅 string 类型可声明 min_length/max_length");
                return;
            }

            if (declaration.MinLength is < 0 or > maximumTextLength
                || declaration.MaxLength is < 0 or > maximumTextLength)
            {
                errors.Add($"指令 '{commandId}' 的参数 '{label}' 长度约束必须在 0 到 65536 之间");
            }
            if (declaration.MinLength > declaration.MaxLength)
                errors.Add($"指令 '{commandId}' 的参数 '{label}' min_length 不能大于 max_length");
        }

        private static void ValidateArgumentEnumValues(
            string commandId,
            string label,
            PluginCommandArgumentDeclaration declaration,
            List<string> errors)
        {
            var values = declaration.EnumValues ?? new List<string>();
            if (declaration.Type != PluginCommandArgumentType.Enum)
            {
                if (values.Count > 0)
                    errors.Add($"指令 '{commandId}' 的参数 '{label}' 仅 enum 类型可声明 enum_values");
                return;
            }

            if (values.Count is < 1 or > 64)
            {
                errors.Add($"指令 '{commandId}' 的枚举参数 '{label}' 必须声明 1 到 64 个 enum_values");
                return;
            }

            var uniqueValues = new HashSet<string>(StringComparer.Ordinal);
            if (values.Any(value =>
                    string.IsNullOrWhiteSpace(value)
                    || value.Length > 1024
                    || !uniqueValues.Add(value)))
            {
                errors.Add($"指令 '{commandId}' 的枚举参数 '{label}' 包含空值、重复值或超长值");
            }
        }

        private static bool IsIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
                && value.Length <= 64
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '.' or '_' or '-');

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
