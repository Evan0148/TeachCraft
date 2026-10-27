using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Import;

/// <summary>导入阶段共用的小工具：合并单元格取值、星期与班级识别、学科列名归一。</summary>
internal static class WeaveImportText
{
    private static readonly char[] BracketChars = ['(', '（', '[', '【', '〔'];

    private static readonly string[] KnownSubjectAliases =
    [
        "语文", "数学", "英语", "物理", "化学", "生物", "政治", "道德与法治", "道法",
        "历史", "地理", "体育", "音乐", "美术", "劳动", "劳动课", "综合", "综合实践",
        "信息技术", "信息科技", "信息", "科学", "心理健康", "心理", "班会", "书法",
        "舞蹈", "编程",
    ];

    private static readonly HashSet<string> KnownSubjects =
        KnownSubjectAliases.Select(WeaveDefaults.SubjectAlias)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> SubjectAliasInputs => KnownSubjectAliases;

    private static readonly string[] ClassNoise =
        ["班级", "班主任", "课时", "人数", "合计", "备注", "说明", "序号",
         "要求", "看班", "值班", "安排", "注意", "通知", "附件", "会议"];

    private static readonly string[] TeacherNoise =
        ["轮流", "安排", "待定", "外聘", "见附件", "附件", "另行", "通知", "学校",
         "教师", "老师", "学科", "备课", "科组", "会议", "活动"];

    public static string Compact(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        return raw
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    public static string GetMergedText(IXLWorksheet sheet, int row, int column)
    {
        if (row < 1 || column < 1) return string.Empty;

        var cell = sheet.Cell(row, column);
        if (cell.IsMerged())
        {
            var range = cell.MergedRange();
            if (range is not null) cell = range.FirstCell();
        }

        return cell.GetString().Trim();
    }

    public static string SubjectName(string? raw)
    {
        var text = Compact(raw);
        if (text.Length == 0) return string.Empty;

        var cut = text.IndexOfAny(BracketChars);
        if (cut > 0) text = text[..cut];

        return WeaveDefaults.SubjectAlias(text);
    }

    public static bool IsKnownSubject(string? raw) =>
        KnownSubjects.Contains(SubjectName(raw));

    public static bool IsMainSubject(string subjectName) =>
        subjectName is "语文" or "数学" or "英语";

    public static bool IsMetaHeader(string? raw)
    {
        var text = Compact(raw);
        if (text.Length == 0) return true;

        if (text is "班级" or "班主任" or "姓名" or "学科" or "兼职岗位" or "联系电话"
            or "备注" or "序号" or "年级" or "合计")
        {
            return true;
        }

        return text.Contains("人数", StringComparison.Ordinal)
               || text.Contains("课时", StringComparison.Ordinal)
               || text.Contains("班级", StringComparison.Ordinal)
               || text.Contains("任教", StringComparison.Ordinal)
               || text.Contains("兼职", StringComparison.Ordinal)
               || text.Contains("合计", StringComparison.Ordinal)
               || text.Contains("总计", StringComparison.Ordinal);
    }

    public static bool LooksLikeClass(string? raw)
    {
        var text = Compact(raw);
        if (text.Length == 0 || text.Length > 20) return false;
        if (text.Contains('：') || text.Contains(':')) return false;
        if (ClassNoise.Any(n => text.Contains(n, StringComparison.Ordinal))) return false;

        if (text.Contains('班')) return true;
        return Regex.IsMatch(text, @"^[七八九一二三四五六]?\d{1,2}$");
    }

    public static bool LooksLikeTeacherName(string? raw)
    {
        var name = Compact(raw);
        if (name.Length is < 2 or > 6) return false;
        if (TeacherNoise.Any(n => name.Contains(n, StringComparison.Ordinal))) return false;

        return name.All(c => (c >= '\u4e00' && c <= '\u9fff') || c is '·' or '•');
    }

    public static int DayIndex(string? raw)
    {
        var text = Compact(raw);
        if (text.Length == 0) return 0;

        var match = Regex.Match(text, "(?:星期|周)([一二三四五])");
        if (!match.Success) return 0;

        return match.Groups[1].Value switch
        {
            "一" => 1,
            "二" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            _ => 0,
        };
    }

    public static List<string> SplitTeacherTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return Regex.Split(raw, @"[/／、，,；;]+")
            .Select(Compact)
            .Where(t => t.Length > 0)
            .ToList();
    }
}
