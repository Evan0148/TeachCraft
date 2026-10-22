using System.Collections.Generic;
using System.Linq;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Layout;

/// <summary>成绩条的全部视觉规则：分档配色、进退步符号、文案模板。</summary>
public static class Strips
{
    public const string FontName = "微软雅黑";

    // 分档底色
    public const string TierTop = "70AD47";      // 班次 1-3
    public const string TierGood = "C6E0B4";     // 4-10
    public const string TierUpper = "DDEBF7";    // 11-20
    public const string TierMiddle = "FFF2CC";   // 21-35
    public const string TierLow = "F8CBAD";      // 36+
    public const string NoData = "F2F2F2";

    // 文字 / 底色
    public const string NameText = "1F3864";
    public const string NameFill = "EAF2FF";
    public const string HeaderText = "595959";
    public const string HeaderFill = "F7F9FC";
    public const string ScoreText = "262626";
    public const string TotalText = "9C5700";
    public const string TotalFill = "FFE699";
    public const string RankFill = "F2F2F2";
    public const string ClassRankFill = "DDEBF7";
    public const string RiseText = "1E7B34";
    public const string FallText = "C00000";
    public const string FlatText = "808080";

    // 边框
    public const string GridLine = "D9DEE7";
    public const string StripEdge = "AAB4C5";

    public const string TotalColumnHeader = "总分";
    public const string SchoolRankHeader = "校次";
    public const string ClassRankHeader = "班次";

    /// <summary>图例中的一段文字，Color 非空时用对应颜色渲染（用于 ■ 色块）。</summary>
    public sealed record LegendSegment(string Text, string? Color = null);

    /// <summary>图例分段：得分底色按班次或校次着色，以及进退步符号说明。</summary>
    public static IReadOnlyList<LegendSegment> LegendSegments(bool useSchoolRank = false, string? comparisonNote = null) => new List<LegendSegment>
    {
        new(useSchoolRank ? "得分底色按【校次】着色：" : "得分底色按【班次】着色："),
        new("■", TierTop), new("1-3名  "),
        new("■", TierGood), new("4-10名  "),
        new("■", TierUpper), new("11-20名  "),
        new("■", TierMiddle), new("21-35名  "),
        new("■", TierLow), new("36名以后      "),
        new(string.IsNullOrWhiteSpace(comparisonNote) ? "进退步：" : $"进退步（{comparisonNote.Trim()}）："),
        new("▲", RiseText), new("进步  "),
        new("▼", FallText), new("退步  "),
        new("—", FlatText), new("无对比数据"),
    };

    public static string LegendText() => string.Concat(LegendSegments().Select(s => s.Text));

    public static readonly string[] TierOrder = { TierTop, TierGood, TierUpper, TierMiddle, TierLow };

    public static string TierFill(int? classRank) => classRank switch
    {
        null or <= 0 => NoData,
        <= 3 => TierTop,
        <= 10 => TierGood,
        <= 20 => TierUpper,
        <= 35 => TierMiddle,
        _ => TierLow,
    };

    /// <summary>深绿底需要白字，其余用深灰字。</summary>
    public static string ScoreTextColor(int? classRank) =>
        classRank is >= 1 and <= 3 ? "FFFFFF" : ScoreText;

    /// <summary>进退步文案：▲n / ▼n / – / —。</summary>
    public static string DeltaText(int? delta) => delta switch
    {
        null => "—",
        > 0 => $"▲{delta.Value}",
        < 0 => $"▼{-delta.Value}",
        _ => "–",
    };

    public static string DeltaColor(int? delta) => delta switch
    {
        > 0 => RiseText,
        < 0 => FallText,
        _ => FlatText,
    };

    /// <summary>科目下方那行：「班4 ▼1」或「校58 ▼21」。</summary>
    public static string SubjectLine(int? rank, int? delta, bool useSchoolRank = false)
    {
        var prefix = useSchoolRank ? "校" : "班";
        var rankStr = rank is > 0 ? rank.Value.ToString() : "—";
        return $"{prefix}{rankStr} {DeltaText(delta)}";
    }

    /// <summary>向后兼容：默认导出班次。</summary>
    public static string SubjectLine(int? classRank, int? classDelta) =>
        SubjectLine(classRank, classDelta, useSchoolRank: false);

    public static string StudentCellText(StudentRecord student) =>
        $"{student.No} {student.Name}\n{student.ExamNo}";
}
