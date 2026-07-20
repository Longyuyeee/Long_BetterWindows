namespace LongBetterWindows.Host.Engine
{
    internal static class WebPluginArguments
    {
        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        internal static string GetString(object?[] args, int index, string defaultValue = "") =>
            args.Length > index ? args[index]?.ToString() ?? defaultValue : defaultValue;

        internal static int GetInt(object?[] args, int index, int defaultValue = 0) =>
            int.TryParse(GetString(args, index), out var value) ? value : defaultValue;

        internal static long GetLong(object?[] args, int index, long defaultValue = 0) =>
            long.TryParse(GetString(args, index), out var value) ? value : defaultValue;

        internal static bool GetBool(object?[] args, int index, bool defaultValue = false) =>
            bool.TryParse(GetString(args, index), out var value) ? value : defaultValue;

        internal static T GetEnum<T>(object?[] args, int index, T defaultValue)
            where T : struct, Enum =>
            Enum.TryParse<T>(GetString(args, index), true, out var value) ? value : defaultValue;

        internal static T? GetJson<T>(object?[] args, int index)
        {
            if (args.Length <= index || args[index] == null)
                return default;

            if (args[index] is System.Text.Json.JsonElement element)
                return System.Text.Json.JsonSerializer.Deserialize<T>(
                    element.GetRawText(), JsonOptions);

            return System.Text.Json.JsonSerializer.Deserialize<T>(
                System.Text.Json.JsonSerializer.Serialize(args[index]),
                JsonOptions);
        }

        internal static List<string> GetStringList(object?[] args, int index) =>
            GetJson<List<string>>(args, index) ?? new List<string>();

        internal static Dictionary<string, string>? GetHeaders(object?[] args, int index)
        {
            try
            {
                return GetJson<Dictionary<string, string>>(args, index);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
