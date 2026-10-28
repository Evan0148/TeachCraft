using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace MeetRoster.Core.Import;

/// <summary>
/// 比赛项目名归一化的唯一实现（Plan 2.2）：报名导入与赛程导入共用。
/// 规则取两份旧实现的并集：全半角折叠（FormKC）→ 符号/空白折叠 → 映射表兜底。
/// 只用于项目名 / 赛次列，不用于姓名、班级名。
/// </summary>
public static class EventNameNormalizer
{
    private static readonly FrozenDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["100米"] = "100米",
            ["200米"] = "200米",
            ["400米"] = "400米",
            ["800米"] = "800米",
            ["1000米"] = "1000米",
            ["100o米"] = "1000米", // 字母 O 冒充 0
            ["4x100米接力"] = "4x100米接力",
            ["20x50米接力"] = "20*50米男女混合接力",
            ["20x50米男女混合接力"] = "20*50米男女混合接力",
            ["20*50米男女混合接力"] = "20*50米男女混合接力",
            ["跳绳"] = "一分钟跳绳",
            ["1分钟跳绳"] = "一分钟跳绳",
            ["一分钟跳绳"] = "一分钟跳绳",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        var text = (value ?? string.Empty)
            .Normalize(NormalizationForm.FormKC) // 全角字母数字括号折叠为半角
            .Replace("\u3000", " ", StringComparison.Ordinal)
            .Trim()
            .Replace("×", "x", StringComparison.Ordinal)
            .Replace("X", "x", StringComparison.Ordinal)
            .Replace("（团体）", "", StringComparison.Ordinal)
            .Replace("(团体)", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("M", "米", StringComparison.OrdinalIgnoreCase);

        return Map.TryGetValue(text, out var mapped) ? mapped : text;
    }
}
