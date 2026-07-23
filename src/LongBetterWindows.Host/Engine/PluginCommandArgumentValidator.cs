using System.Globalization;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Engine
{
    /// <summary>
    /// 命令参数的统一无 UI 验证器。调用值继续使用字符串协议，验证成功后返回规范化副本。
    /// </summary>
    public static class PluginCommandArgumentValidator
    {
        public static PluginCommandArgumentValidationResult Validate(
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema,
            IReadOnlyDictionary<string, string>? arguments)
            => ValidateCore(
                schema,
                arguments,
                applyDefaults: true,
                requireRequiredValues: true,
                deferredKeys: null);

        /// <summary>
        /// 验证工作流审查时可确定的字面参数。延迟绑定键可满足必填项，
        /// 其动态值将在绑定解析后再次执行完整验证。
        /// </summary>
        public static PluginCommandArgumentValidationResult ValidateForWorkflowPreflight(
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema,
            IReadOnlyDictionary<string, string>? arguments,
            IEnumerable<string>? deferredArgumentKeys)
            => ValidateCore(
                schema,
                arguments,
                applyDefaults: true,
                requireRequiredValues: true,
                deferredKeys: deferredArgumentKeys is null
                    ? null
                    : new HashSet<string>(deferredArgumentKeys, StringComparer.Ordinal));

        /// <summary>验证 Manifest 中声明的默认值，不要求每个必填参数都提供默认值。</summary>
        public static PluginCommandArgumentValidationResult ValidateDeclaredDefaults(
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema)
        {
            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var declaration in schema ?? Array.Empty<PluginCommandArgumentDeclaration>())
            {
                if (declaration is not null
                    && !string.IsNullOrWhiteSpace(declaration.Key)
                    && declaration.DefaultValue is not null)
                {
                    defaults.TryAdd(declaration.Key, declaration.DefaultValue);
                }
            }
            return ValidateCore(
                schema,
                defaults,
                applyDefaults: false,
                requireRequiredValues: false,
                deferredKeys: null);
        }

        private static PluginCommandArgumentValidationResult ValidateCore(
            IReadOnlyList<PluginCommandArgumentDeclaration>? schema,
            IReadOnlyDictionary<string, string>? arguments,
            bool applyDefaults,
            bool requireRequiredValues,
            IReadOnlySet<string>? deferredKeys)
        {
            arguments ??= new Dictionary<string, string>();
            if (schema is null || schema.Count == 0)
                return PluginCommandArgumentValidationResult.Success(arguments);

            var issues = new List<string>();
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            var declarations = new Dictionary<string, PluginCommandArgumentDeclaration>(
                StringComparer.Ordinal);
            foreach (var declaration in schema)
            {
                if (declaration is null
                    || string.IsNullOrWhiteSpace(declaration.Key)
                    || !declarations.TryAdd(declaration.Key, declaration))
                {
                    issues.Add("参数 Schema 无效，无法验证命令参数。");
                }
            }

            IEnumerable<string> suppliedKeys = arguments.Keys;
            if (deferredKeys is not null)
                suppliedKeys = suppliedKeys.Concat(deferredKeys);
            suppliedKeys = suppliedKeys
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal);
            foreach (var key in suppliedKeys)
            {
                if (!declarations.ContainsKey(key))
                    issues.Add($"未声明参数: '{key}'。");
            }

            foreach (var declaration in schema)
            {
                if (declaration is null
                    || string.IsNullOrWhiteSpace(declaration.Key)
                    || !declarations.TryGetValue(declaration.Key, out var registered)
                    || !ReferenceEquals(declaration, registered))
                {
                    continue;
                }

                if (deferredKeys?.Contains(declaration.Key) == true)
                    continue;

                string? value;
                if (!arguments.TryGetValue(declaration.Key, out value))
                {
                    value = applyDefaults ? declaration.DefaultValue : null;
                    if (value is null)
                    {
                        if (requireRequiredValues && declaration.Required)
                            issues.Add($"参数 '{declaration.Key}' 为必填项。");
                        continue;
                    }
                }

                if (value is null)
                {
                    issues.Add($"参数 '{declaration.Key}' 不能为 null。");
                    continue;
                }

                if (TryNormalize(declaration, value, out var normalizedValue, out var error))
                    normalized[declaration.Key] = normalizedValue;
                else
                    issues.Add(error);
            }

            return issues.Count == 0
                ? PluginCommandArgumentValidationResult.Success(normalized)
                : PluginCommandArgumentValidationResult.Failure(issues);
        }

        private static bool TryNormalize(
            PluginCommandArgumentDeclaration declaration,
            string value,
            out string normalized,
            out string error)
        {
            normalized = value;
            error = string.Empty;
            switch (declaration.Type)
            {
                case PluginCommandArgumentType.String:
                    return ValidateString(declaration, value, out error);

                case PluginCommandArgumentType.Integer:
                    if (!long.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var integer))
                    {
                        error = $"参数 '{declaration.Key}' 必须是整数。";
                        return false;
                    }
                    if (!ValidateNumericRange(declaration, integer, out error))
                        return false;
                    normalized = integer.ToString(CultureInfo.InvariantCulture);
                    return true;

                case PluginCommandArgumentType.Number:
                    if (!decimal.TryParse(
                            value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var number))
                    {
                        error = $"参数 '{declaration.Key}' 必须是有限数值。";
                        return false;
                    }
                    if (!ValidateNumericRange(declaration, number, out error))
                        return false;
                    normalized = number.ToString("G29", CultureInfo.InvariantCulture);
                    return true;

                case PluginCommandArgumentType.Boolean:
                    if (!bool.TryParse(value, out var boolean))
                    {
                        error = $"参数 '{declaration.Key}' 必须是 true 或 false。";
                        return false;
                    }
                    normalized = boolean ? "true" : "false";
                    return true;

                case PluginCommandArgumentType.Enum:
                    if (!(declaration.EnumValues ?? new List<string>()).Contains(
                            value,
                            StringComparer.Ordinal))
                    {
                        error = $"参数 '{declaration.Key}' 不在允许的枚举值中。";
                        return false;
                    }
                    return true;

                default:
                    error = $"参数 '{declaration.Key}' 的类型声明无效。";
                    return false;
            }
        }

        private static bool ValidateString(
            PluginCommandArgumentDeclaration declaration,
            string value,
            out string error)
        {
            error = string.Empty;
            if (declaration.MinLength.HasValue && value.Length < declaration.MinLength.Value)
            {
                error = $"参数 '{declaration.Key}' 长度不能小于 {declaration.MinLength.Value}。";
                return false;
            }
            if (declaration.MaxLength.HasValue && value.Length > declaration.MaxLength.Value)
            {
                error = $"参数 '{declaration.Key}' 长度不能大于 {declaration.MaxLength.Value}。";
                return false;
            }
            return true;
        }

        private static bool ValidateNumericRange(
            PluginCommandArgumentDeclaration declaration,
            decimal value,
            out string error)
        {
            error = string.Empty;
            if (declaration.Minimum.HasValue && value < declaration.Minimum.Value)
            {
                error = $"参数 '{declaration.Key}' 不能小于 {declaration.Minimum.Value.ToString(CultureInfo.InvariantCulture)}。";
                return false;
            }
            if (declaration.Maximum.HasValue && value > declaration.Maximum.Value)
            {
                error = $"参数 '{declaration.Key}' 不能大于 {declaration.Maximum.Value.ToString(CultureInfo.InvariantCulture)}。";
                return false;
            }
            return true;
        }
    }

    public sealed class PluginCommandArgumentValidationResult
    {
        private PluginCommandArgumentValidationResult(
            bool isSuccess,
            IReadOnlyDictionary<string, string> arguments,
            IReadOnlyList<string> issues)
        {
            IsSuccess = isSuccess;
            Arguments = arguments;
            Issues = issues;
        }

        public bool IsSuccess { get; }
        public IReadOnlyDictionary<string, string> Arguments { get; }
        public IReadOnlyList<string> Issues { get; }

        public static PluginCommandArgumentValidationResult Success(
            IReadOnlyDictionary<string, string> arguments)
            => new(
                true,
                new Dictionary<string, string>(arguments, StringComparer.Ordinal),
                Array.Empty<string>());

        public static PluginCommandArgumentValidationResult Failure(
            IReadOnlyList<string> issues)
            => new(
                false,
                new Dictionary<string, string>(),
                issues.ToArray());
    }
}
