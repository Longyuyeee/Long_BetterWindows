using System.IO;
using System.Text.Json;
using Serilog;

namespace LongBetterWindows.Host.Services
{
    /// <summary>
    /// 国际化服务——JSON 文件驱动的多语言支持。
    /// 后续可通过设置界面切换语言。
    /// </summary>
    public static class I18nService
    {
        private static Dictionary<string, string> _strings = new();
        public static string CurrentLang { get; private set; } = "zh-CN";

        /// <summary>支持的语言列表</summary>
        public static readonly string[] SupportedLanguages = { "zh-CN", "en-US" };

        public static void Initialize(string? lang = null)
        {
            CurrentLang = lang ?? "zh-CN";
            Load(CurrentLang);
        }

        public static string T(string key, string fallback = "")
        {
            return _strings.TryGetValue(key, out var val) ? val : fallback;
        }

        public static void SetLanguage(string lang)
        {
            CurrentLang = lang;
            Load(lang);
            Log.Information("语言切换为: {Lang}", lang);
        }

        /// <summary>在中英文之间切换</summary>
        public static string ToggleLanguage()
        {
            var next = CurrentLang == "zh-CN" ? "en-US" : "zh-CN";
            SetLanguage(next);
            return next;
        }

        private static void Load(string lang)
        {
            _strings.Clear();

            // 查找 i18n 目录
            var dir = AppContext.BaseDirectory;
            string? i18nDir = null;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "i18n");
                if (Directory.Exists(candidate)) { i18nDir = candidate; break; }
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }

            if (i18nDir == null) return;

            var file = Path.Combine(i18nDir, $"{lang}.json");
            if (!File.Exists(file)) file = Path.Combine(i18nDir, "zh-CN.json");
            if (!File.Exists(file)) return;

            try
            {
                var json = File.ReadAllText(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null) _strings = dict;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载语言文件失败: {Lang}", lang);
            }
        }
    }
}
