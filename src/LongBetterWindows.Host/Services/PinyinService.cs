using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class PinyinService : IPinyinService
    {
        // 简化的拼音映射表（生产环境应使用完整的拼音库）
        private static readonly Dictionary<char, string> PinyinMap = new()
        {
            {'的', "de"}, {'一', "yi"}, {'是', "shi"}, {'不', "bu"}, {'了', "le"},
            {'人', "ren"}, {'我', "wo"}, {'在', "zai"}, {'有', "you"}, {'他', "ta"},
            {'这', "zhe"}, {'为', "wei"}, {'之', "zhi"}, {'大', "da"}, {'来', "lai"},
            {'以', "yi"}, {'个', "ge"}, {'中', "zhong"}, {'上', "shang"}, {'们', "men"},
            {'到', "dao"}, {'说', "shuo"}, {'国', "guo"}, {'和', "he"}, {'地', "di"},
            {'也', "ye"}, {'子', "zi"}, {'时', "shi"}, {'道', "dao"}, {'出', "chu"},
            {'而', "er"}, {'要', "yao"}, {'于', "yu"}, {'就', "jiu"}, {'下', "xia"},
            {'得', "de"}, {'可', "ke"}, {'你', "ni"}, {'年', "nian"}, {'生', "sheng"},
            {'自', "zi"}, {'会', "hui"}, {'那', "na"}, {'后', "hou"}, {'能', "neng"},
            {'对', "dui"}, {'着', "zhe"}, {'事', "shi"}, {'其', "qi"}, {'里', "li"},
            {'所', "suo"}, {'去', "qu"}, {'行', "xing"}, {'过', "guo"}, {'家', "jia"},
            {'十', "shi"}, {'用', "yong"}, {'发', "fa"}, {'天', "tian"}, {'如', "ru"},
            {'然', "ran"}, {'作', "zuo"}, {'方', "fang"}, {'成', "cheng"}, {'者', "zhe"},
            {'多', "duo"}, {'日', "ri"}, {'都', "dou"}, {'三', "san"}, {'小', "xiao"},
            {'军', "jun"}, {'二', "er"}, {'无', "wu"}, {'同', "tong"}, {'么', "me"},
            {'经', "jing"}, {'法', "fa"}, {'当', "dang"}, {'起', "qi"}, {'与', "yu"},
            {'好', "hao"}, {'看', "kan"}, {'学', "xue"}, {'进', "jin"}, {'种', "zhong"},
            {'将', "jiang"}, {'还', "hai"}, {'分', "fen"}, {'此', "ci"}, {'心', "xin"},
            {'前', "qian"}, {'面', "mian"}, {'又', "you"}, {'定', "ding"}, {'见', "jian"},
            {'只', "zhi"}, {'主', "zhu"}, {'没', "mei"}, {'公', "gong"}, {'从', "cong"},
            {'文', "wen"}, {'开', "kai"}, {'手', "shou"}, {'十', "shi"}, {'如', "ru"},
            {'现', "xian"}, {'本', "ben"}, {'月', "yue"}, {'明', "ming"}, {'打', "da"},
            {'电', "dian"}, {'脑', "nao"}, {'文', "wen"}, {'件', "jian"}, {'夹', "jia"},
            {'系', "xi"}, {'统', "tong"}, {'设', "she"}, {'置', "zhi"}, {'启', "qi"},
            {'动', "dong"}, {'器', "qi"}, {'应', "ying"}, {'程', "cheng"}, {'序', "xu"}
        };

        public Task<HostApiResponse<string>> GetPinyinAsync(string text)
        {
            return Task.Run(() =>
            {
                try
                {
                    var pinyin = string.Join("", text.Select(c =>
                        PinyinMap.ContainsKey(c) ? PinyinMap[c] : c.ToString()));
                    return HostApiResponse<string>.Success(pinyin);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<string>> GetPinyinInitialsAsync(string text)
        {
            return Task.Run(() =>
            {
                try
                {
                    var initials = string.Join("", text.Select(c =>
                        PinyinMap.ContainsKey(c) ? PinyinMap[c][0].ToString() : c.ToString()));
                    return HostApiResponse<string>.Success(initials);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<bool>> MatchAsync(string text, string query)
        {
            return Task.Run(() =>
            {
                try
                {
                    var lowerQuery = query.ToLower();

                    // 直接匹配
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                        return HostApiResponse<bool>.Success(true);

                    // 拼音全拼匹配
                    var pinyin = string.Join("", text.Select(c =>
                        PinyinMap.ContainsKey(c) ? PinyinMap[c] : c.ToString())).ToLower();
                    if (pinyin.Contains(lowerQuery))
                        return HostApiResponse<bool>.Success(true);

                    // 拼音首字母匹配
                    var initials = string.Join("", text.Select(c =>
                        PinyinMap.ContainsKey(c) ? PinyinMap[c][0].ToString() : c.ToString())).ToLower();
                    if (initials.Contains(lowerQuery))
                        return HostApiResponse<bool>.Success(true);

                    return HostApiResponse<bool>.Success(false);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<bool>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<PinyinMatchResult>>> FilterAsync(List<string> items, string query)
        {
            return Task.Run(() =>
            {
                try
                {
                    var results = new List<PinyinMatchResult>();
                    var lowerQuery = query.ToLower();

                    foreach (var item in items)
                    {
                        var pinyin = string.Join("", item.Select(c =>
                            PinyinMap.ContainsKey(c) ? PinyinMap[c] : c.ToString())).ToLower();
                        var initials = string.Join("", item.Select(c =>
                            PinyinMap.ContainsKey(c) ? PinyinMap[c][0].ToString() : c.ToString())).ToLower();

                        bool isMatch = false;
                        int score = 0;

                        // 直接匹配（最高优先级）
                        if (item.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                            score = 100;
                        }
                        // 拼音全拼匹配
                        else if (pinyin.Contains(lowerQuery))
                        {
                            isMatch = true;
                            score = 80;
                        }
                        // 拼音首字母匹配
                        else if (initials.Contains(lowerQuery))
                        {
                            isMatch = true;
                            score = 60;
                        }

                        results.Add(new PinyinMatchResult
                        {
                            Text = item,
                            Pinyin = pinyin,
                            Initials = initials,
                            IsMatch = isMatch,
                            Score = score
                        });
                    }

                    var filtered = results
                        .Where(r => r.IsMatch)
                        .OrderByDescending(r => r.Score)
                        .ToList();

                    return HostApiResponse<List<PinyinMatchResult>>.Success(filtered);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<PinyinMatchResult>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }
    }
}
