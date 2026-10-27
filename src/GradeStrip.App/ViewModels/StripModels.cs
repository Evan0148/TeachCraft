using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Media;
using GradeStrip.Core.Layout;
using GradeStrip.Core.Model;

namespace GradeStrip.App.ViewModels;

public sealed class PreviewItem
{
    public bool IsHeader { get; init; }
    public bool IsTitleBlock { get; init; }
    public string HeaderText { get; init; } = string.Empty;
    public string HeaderMeta { get; init; } = string.Empty;
    public string TitleText { get; init; } = string.Empty;
    public bool HasLegend { get; init; }
    public bool UseSchoolRank { get; init; }
    public string LegendRankText => UseSchoolRank ? "得分底色按校次着色" : "得分底色按班次着色";
    public IReadOnlyList<LegendItem> TierLegend => PreviewLegend.Tiers;
    public IReadOnlyList<LegendItem> DeltaLegend => PreviewLegend.Deltas;
    public bool IsStrip => !IsHeader && !IsTitleBlock;
    public StripRow? Strip { get; init; }
}

/// <summary>界面图例中的一个小色块及其说明文字。</summary>
public sealed class LegendItem
{
    public string Text { get; init; } = string.Empty;
    public IBrush Fill { get; init; } = Palette.White;
    public IBrush Foreground { get; init; } = Palette.Muted;
}

/// <summary>预览图例：色块直接取成绩条的分档/进退步配色。</summary>
public static class PreviewLegend
{
    public static readonly IReadOnlyList<LegendItem> Tiers = new[]
    {
        new LegendItem { Text = "1-3名", Fill = Palette.Brush(Strips.TierTop), Foreground = Palette.InkSoft },
        new LegendItem { Text = "4-10名", Fill = Palette.Brush(Strips.TierGood), Foreground = Palette.InkSoft },
        new LegendItem { Text = "11-20名", Fill = Palette.Brush(Strips.TierUpper), Foreground = Palette.InkSoft },
        new LegendItem { Text = "21-35名", Fill = Palette.Brush(Strips.TierMiddle), Foreground = Palette.InkSoft },
        new LegendItem { Text = "36名以后", Fill = Palette.Brush(Strips.TierLow), Foreground = Palette.InkSoft },
    };

    public static readonly IReadOnlyList<LegendItem> Deltas = new[]
    {
        new LegendItem { Text = "▲ 进步", Foreground = Palette.Brush(Strips.RiseText) },
        new LegendItem { Text = "▼ 退步", Foreground = Palette.Brush(Strips.FallText) },
        new LegendItem { Text = "— 无对比数据", Foreground = Palette.Brush(Strips.FlatText) },
    };
}

/// <summary>界面预览用的一条成绩条，样式规则全部取自 <see cref="Strips"/>。</summary>
public sealed class StripRow
{
    public string NoText { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ExamNo { get; init; } = string.Empty;
    public string TotalText { get; init; } = string.Empty;
    public string SchoolRankText { get; init; } = string.Empty;
    public string ClassRankText { get; init; } = string.Empty;
    public string SchoolDeltaText { get; init; } = string.Empty;
    public IBrush SchoolDeltaBrush { get; init; } = Palette.Muted;
    public string ClassDeltaText { get; init; } = string.Empty;
    public IBrush ClassDeltaBrush { get; init; } = Palette.Muted;
    public bool UseSchoolRank { get; init; }
    public IBrush SchoolRankFill => UseSchoolRank ? Palette.Brush(Strips.ClassRankFill) : Palette.Brush(Strips.RankFill);
    public IBrush SchoolRankForeground => UseSchoolRank ? Palette.Brush(Strips.NameText) : Palette.Brush(Strips.ScoreText);
    public IBrush ClassRankFill => !UseSchoolRank ? Palette.Brush(Strips.ClassRankFill) : Palette.Brush(Strips.RankFill);
    public IBrush ClassRankForeground => !UseSchoolRank ? Palette.Brush(Strips.NameText) : Palette.Brush(Strips.ScoreText);
    public IReadOnlyList<StripCell> Cells { get; init; } = Array.Empty<StripCell>();

    public static StripRow From(StudentRecord student, IReadOnlyList<string> subjects, bool useSchoolRank = false)
    {
        var cells = new List<StripCell>(subjects.Count);
        foreach (var subject in subjects)
        {
            var score = student[subject];
            var rank = useSchoolRank ? score.SchoolRank : score.ClassRank;
            var delta = useSchoolRank ? score.SchoolDelta : score.ClassDelta;
            cells.Add(new StripCell
            {
                Subject = subject,
                ScoreText = score.Score is null ? "—" : Format(score.Score.Value),
                Fill = Palette.Brush(Strips.TierFill(rank)),
                ScoreBrush = Palette.Brush(Strips.ScoreTextColor(rank)),
                Note = Strips.SubjectLine(rank, delta, useSchoolRank),
                NoteBrush = Palette.Brush(Strips.DeltaColor(delta)),
            });
        }

        return new StripRow
        {
            NoText = student.No.ToString(CultureInfo.InvariantCulture),
            Name = student.Name,
            ExamNo = student.ExamNo,
            TotalText = student.Total.Score is null ? "—" : Format(student.Total.Score.Value),
            SchoolRankText = student.Total.SchoolRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
            ClassRankText = student.Total.ClassRank?.ToString(CultureInfo.InvariantCulture) ?? "—",
            SchoolDeltaText = Strips.DeltaText(student.Total.SchoolDelta),
            SchoolDeltaBrush = Palette.Brush(Strips.DeltaColor(student.Total.SchoolDelta)),
            ClassDeltaText = Strips.DeltaText(student.Total.ClassDelta),
            ClassDeltaBrush = Palette.Brush(Strips.DeltaColor(student.Total.ClassDelta)),
            UseSchoolRank = useSchoolRank,
            Cells = cells,
        };
    }

    private static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed class StripCell
{
    public string Subject { get; init; } = string.Empty;
    public string ScoreText { get; init; } = string.Empty;
    public IBrush Fill { get; init; } = Palette.White;
    public IBrush ScoreBrush { get; init; } = Palette.InkSoft;
    public string Note { get; init; } = string.Empty;
    public IBrush NoteBrush { get; init; } = Palette.Muted;
}
