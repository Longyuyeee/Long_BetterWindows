using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IPinyinService
    {
        /// <summary>获取汉字的拼音（全拼）</summary>
        Task<HostApiResponse<string>> GetPinyinAsync(string text);

        /// <summary>获取汉字的拼音首字母</summary>
        Task<HostApiResponse<string>> GetPinyinInitialsAsync(string text);

        /// <summary>拼音模糊匹配（支持全拼、简拼、首字母）</summary>
        Task<HostApiResponse<bool>> MatchAsync(string text, string query);

        /// <summary>批量拼音匹配过滤</summary>
        Task<HostApiResponse<List<PinyinMatchResult>>> FilterAsync(List<string> items, string query);
    }

    public class PinyinMatchResult
    {
        public string Text { get; set; } = "";
        public string Pinyin { get; set; } = "";
        public string Initials { get; set; } = "";
        public bool IsMatch { get; set; }
        public int Score { get; set; }
    }
}
