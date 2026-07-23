using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Engine;

namespace LongBetterWindows.Tests;

public class PluginCommandArgumentValidatorTests
{
    [Fact]
    public void Validate_AppliesDefaultsAndCanonicalizesProtocolValues()
    {
        var schema = new[]
        {
            Declaration("count", PluginCommandArgumentType.Integer, defaultValue: "0010"),
            Declaration("ratio", PluginCommandArgumentType.Number),
            Declaration("enabled", PluginCommandArgumentType.Boolean),
            Declaration(
                "mode",
                PluginCommandArgumentType.Enum,
                enumValues: ["standard", "compact"]),
        };

        var result = PluginCommandArgumentValidator.Validate(
            schema,
            new Dictionary<string, string>
            {
                ["ratio"] = "1.2500",
                ["enabled"] = "TRUE",
                ["mode"] = "compact",
            });

        Assert.True(result.IsSuccess, string.Join(" ", result.Issues));
        Assert.Equal("10", result.Arguments["count"]);
        Assert.Equal("1.25", result.Arguments["ratio"]);
        Assert.Equal("true", result.Arguments["enabled"]);
        Assert.Equal("compact", result.Arguments["mode"]);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredAndUnknownKeys()
    {
        var schema = new[]
        {
            Declaration("required", PluginCommandArgumentType.String, required: true),
        };

        var result = PluginCommandArgumentValidator.Validate(
            schema,
            new Dictionary<string, string> { ["unknown"] = "private-value" });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Contains("required", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue => issue.Contains("private-value", StringComparison.Ordinal));
        Assert.Empty(result.Arguments);
    }

    [Fact]
    public void Validate_EnforcesTypesEnumsAndFiniteConstraintsWithoutEchoingValues()
    {
        var schema = new[]
        {
            Declaration(
                "integer",
                PluginCommandArgumentType.Integer,
                minimum: 1,
                maximum: 10),
            Declaration(
                "number",
                PluginCommandArgumentType.Number,
                minimum: 0,
                maximum: 1),
            Declaration("flag", PluginCommandArgumentType.Boolean),
            Declaration(
                "mode",
                PluginCommandArgumentType.Enum,
                enumValues: ["safe"]),
            Declaration(
                "token",
                PluginCommandArgumentType.String,
                minLength: 8,
                maxLength: 16,
                sensitive: true),
        };
        var result = PluginCommandArgumentValidator.Validate(
            schema,
            new Dictionary<string, string>
            {
                ["integer"] = "11",
                ["number"] = "NaN",
                ["flag"] = "yes",
                ["mode"] = "SAFE",
                ["token"] = "secret",
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(5, result.Issues.Count);
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Contains("secret", StringComparison.Ordinal)
            || issue.Contains("NaN", StringComparison.Ordinal)
            || issue.Contains("SAFE", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LegacyCommandKeepsFreeFormArgumentsAndReturnsDefensiveCopy()
    {
        var source = new Dictionary<string, string>
        {
            ["custom-key"] = "custom-value",
        };

        var result = PluginCommandArgumentValidator.Validate(
            Array.Empty<PluginCommandArgumentDeclaration>(),
            source);
        source["custom-key"] = "changed";

        Assert.True(result.IsSuccess);
        Assert.Equal("custom-value", result.Arguments["custom-key"]);
    }

    [Fact]
    public void ValidateDeclaredDefaults_DoesNotRequireUnrelatedRequiredValues()
    {
        var valid = PluginCommandArgumentValidator.ValidateDeclaredDefaults(
        [
            Declaration("required", PluginCommandArgumentType.String, required: true),
            Declaration("count", PluginCommandArgumentType.Integer, defaultValue: "10"),
        ]);
        var invalid = PluginCommandArgumentValidator.ValidateDeclaredDefaults(
        [
            Declaration("count", PluginCommandArgumentType.Integer, defaultValue: "not-an-integer"),
        ]);

        Assert.True(valid.IsSuccess, string.Join(" ", valid.Issues));
        Assert.False(invalid.IsSuccess);
        Assert.Contains(invalid.Issues, issue => issue.Contains("count", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForWorkflowPreflight_DefersBoundValuesButRejectsUnknownTargets()
    {
        var schema = new[]
        {
            Declaration(
                "count",
                PluginCommandArgumentType.Integer,
                required: true,
                minimum: 1,
                maximum: 100),
        };

        var deferred = PluginCommandArgumentValidator.ValidateForWorkflowPreflight(
            schema,
            new Dictionary<string, string> { ["count"] = "unused-invalid-literal" },
            ["count"]);
        var unknown = PluginCommandArgumentValidator.ValidateForWorkflowPreflight(
            schema,
            new Dictionary<string, string>(),
            ["missing"]);

        Assert.True(deferred.IsSuccess, string.Join(" ", deferred.Issues));
        Assert.False(unknown.IsSuccess);
        Assert.Contains(unknown.Issues, issue => issue.Contains("missing", StringComparison.Ordinal));
    }

    private static PluginCommandArgumentDeclaration Declaration(
        string key,
        PluginCommandArgumentType type,
        bool required = false,
        string? defaultValue = null,
        bool sensitive = false,
        decimal? minimum = null,
        decimal? maximum = null,
        int? minLength = null,
        int? maxLength = null,
        IReadOnlyList<string>? enumValues = null)
        => new()
        {
            Key = key,
            Name = key,
            Type = type,
            Required = required,
            DefaultValue = defaultValue,
            Sensitive = sensitive,
            Minimum = minimum,
            Maximum = maximum,
            MinLength = minLength,
            MaxLength = maxLength,
            EnumValues = enumValues?.ToList() ?? new List<string>(),
        };
}
