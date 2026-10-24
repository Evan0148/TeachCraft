using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GradeStrip.App.ViewModels;

/// <summary>界面的配色常量，与导出成绩条共用同一套色值。</summary>
public static class Palette
{
    /// <summary>按色值缓存画刷，避免 3000 人 × 多科目时产生十几万个 SolidColorBrush。</summary>
    private static readonly ConcurrentDictionary<string, IBrush> Cache = new(StringComparer.Ordinal);

    public static readonly IBrush Ink = Brush("141413");
    public static readonly IBrush InkSoft = Brush("3D3B36");
    public static readonly IBrush Muted = Brush("85806F");
    public static readonly IBrush Paper = Brush("FAF9F5");
    public static readonly IBrush PaperDeep = Brush("F4F2EC");
    public static readonly IBrush Card = Brush("FFFFFF");
    public static readonly IBrush Line = Brush("E7E3D8");
    public static readonly IBrush Moss = Brush("5A7A4B");
    public static readonly IBrush Seal = Brush("C6613F");
    public static readonly IBrush Accent = Brush("D97757");
    public static readonly IBrush Clay = Brush("D97757");
    public static readonly IBrush AccentDeep = Brush("C6613F");
    public static readonly IBrush White = Brushes.White;

    public static IBrush Brush(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Brushes.White;
        return Cache.GetOrAdd(hex, static key => new SolidColorBrush(Color.Parse("#" + key)));
    }
}

public sealed class KpiCard
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public IBrush Accent { get; init; } = Palette.Ink;
}

public sealed class SubjectStat
{
    public string Name { get; init; } = string.Empty;
    public string AverageText { get; init; } = string.Empty;

    /// <summary>该科满分，用于说明平均分的参照。</summary>
    public string FullMarkText { get; init; } = string.Empty;

    public string RangeText { get; init; } = string.Empty;

    /// <summary>条形填充权重（相对满分，即得分率）。</summary>
    public double FillWeight { get; init; }

    /// <summary>条形空余权重。</summary>
    public double RestWeight { get; init; }

    public IBrush Accent { get; init; } = Palette.Ink;
}

public sealed class TierStat
{
    public string Label { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string PercentText { get; init; } = string.Empty;
    public double Weight { get; init; }
    public IBrush Fill { get; init; } = Palette.Ink;
    public IBrush Foreground { get; init; } = Palette.Ink;
}

public sealed class TrendStat
{
    public string Label { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public IBrush Accent { get; init; } = Palette.Muted;
}

public sealed class StudentRow
{
    public int No { get; init; }
    public string Index { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TotalText { get; init; } = string.Empty;
    public string SchoolRankText { get; init; } = string.Empty;
    public string ClassRankText { get; init; } = string.Empty;
    public string TrendText { get; init; } = string.Empty;
    public IBrush TrendBrush { get; init; } = Palette.Muted;
    public double BarWeight { get; init; }
    public double RestWeight { get; init; }
    public IBrush BarBrush { get; init; } = Palette.Ink;

    /// <summary>跨班考试时姓名后的微型班级角标；单班或该生无班级信息时为空。</summary>
    public string ClassBadgeText { get; init; } = string.Empty;
    public bool HasClassBadge => !string.IsNullOrWhiteSpace(ClassBadgeText);
}

/// <summary>学生画像：个人成绩与班级参照的对照。</summary>
public sealed class StudentDetail
{
    public int No { get; init; }
    public string NoText { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ExamNo { get; init; } = string.Empty;

    public string TotalText { get; init; } = string.Empty;
    public string TotalRateText { get; init; } = string.Empty;
    public string ClassRankText { get; init; } = string.Empty;
    public string SchoolRankText { get; init; } = string.Empty;
    public string ClassRankNote { get; init; } = string.Empty;
    public string SchoolRankNote { get; init; } = string.Empty;

    /// <summary>是否有有效班级名次（缺考/无成绩为 false）。</summary>
    public bool HasRank { get; init; }

    /// <summary>班级位置提示文案（如缺考时显示提示）。</summary>
    public string PositionNote { get; init; } = string.Empty;

    /// <summary>班级名次位置（0~1，用于位置条）。</summary>
    public double PositionWeight { get; init; }
    public double PositionRest { get; init; }

    public string AboveText { get; init; } = string.Empty;
    public string BelowText { get; init; } = string.Empty;
    public IBrush BelowBrush { get; init; } = Palette.Muted;
    public string StrongestText { get; init; } = string.Empty;
    public string WeakestText { get; init; } = string.Empty;
    public string TrendSummary { get; init; } = string.Empty;
    public IBrush TrendBrush { get; init; } = Palette.Muted;

    public IReadOnlyList<DetailSubjectRow> Subjects { get; init; } = Array.Empty<DetailSubjectRow>();

    /// <summary>得分明细（单科个人成绩单导入时可用），逐题对照班均。</summary>
    public IReadOnlyList<QuestionDetailRow> QuestionRows { get; init; } = Array.Empty<QuestionDetailRow>();
    public bool HasQuestionScores => QuestionRows.Count > 0;

    /// <summary>多考综合轨迹档案（满 3 次考试时激活）。</summary>
    public MultiExamProfile? MultiExamProfile { get; init; }
    public bool HasMultiExam => MultiExamProfile is not null && MultiExamProfile.IsAvailable;
}

/// <summary>学生画像里的单题得分明细行。条形＝个人得分率，刻度＝班均得分率（满分按全班最高分推断）。</summary>
public sealed class QuestionDetailRow
{
    public string Label { get; init; } = string.Empty;
    public string ScoreText { get; init; } = string.Empty;
    public string FullText { get; init; } = string.Empty;
    public string RateText { get; init; } = string.Empty;

    /// <summary>个人得分率（条形长度）。</summary>
    public double FillWeight { get; init; }

    /// <summary>班均得分率（刻度位置）。</summary>
    public double AverageWeight { get; init; }

    public bool AboveAverage { get; init; }
    public IBrush FillBrush { get; init; } = Palette.Ink;
}

public sealed class DetailSubjectRow
{
    public string Name { get; init; } = string.Empty;
    public string ScoreText { get; init; } = string.Empty;
    public string FullText { get; init; } = string.Empty;
    public string RateText { get; init; } = string.Empty;

    /// <summary>学生得分率。</summary>
    public double FillWeight { get; init; }

    /// <summary>班级均分得分率（刻度位置）。</summary>
    public double AverageWeight { get; init; }

    public string ClassRankText { get; init; } = string.Empty;
    public string SchoolRankText { get; init; } = string.Empty;
    public string DeltaText { get; init; } = string.Empty;
    public IBrush DeltaBrush { get; init; } = Palette.Muted;
    public bool AboveAverage { get; init; }
    public IBrush FillBrush { get; init; } = Palette.Ink;
}

/// <summary>单科分析：某一科目的全班成绩表现与分布画像。</summary>
public sealed class SubjectDetail : ObservableObject
{
    public string SubjectName { get; init; } = string.Empty;
    public string FullMarkText { get; init; } = string.Empty;
    public string AverageText { get; init; } = string.Empty;
    public string AverageRateText { get; init; } = string.Empty;
    public string HighestText { get; init; } = string.Empty;
    public string HighestStudentText { get; init; } = string.Empty;
    public string LowestText { get; init; } = string.Empty;
    public string RangeText { get; init; } = string.Empty;
    public string PassRateText { get; init; } = string.Empty;
    public string PassCountText { get; init; } = string.Empty;
    public string ExcellentRateText { get; init; } = string.Empty;
    public string RankAmongSubjectsText { get; init; } = string.Empty;
    public string SummaryInsight { get; init; } = string.Empty;

    /// <summary>年级背景与学段定位标签，如「九年级 (初三) · 中考决胜冲刺期」。</summary>
    public string GradeBadge { get; init; } = string.Empty;

    /// <summary>宏观达标评估（均分、及格率与常模对比）。</summary>
    public string MacroAssessment { get; init; } = string.Empty;

    /// <summary>两极分化度评价（极差与离散度）。</summary>
    public string PolarizationAssessment { get; init; } = string.Empty;

    /// <summary>临界生诊断（及格线边缘、特优线边缘人数与名单建议）。</summary>
    public string BorderlineAssessment { get; init; } = string.Empty;

    /// <summary>分层教学与提分建议。</summary>
    public IReadOnlyList<string> ActionStrategies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<TierStat> Tiers { get; init; } = Array.Empty<TierStat>();
    public IReadOnlyList<SubjectStudentRow> Students { get; init; } = Array.Empty<SubjectStudentRow>();

    private IReadOnlyList<SubjectStudentRow> _visibleStudents = Array.Empty<SubjectStudentRow>();

    /// <summary>单科学生排名实际展示的行（超过折叠阈值时是 Students 的前 N 项）。</summary>
    public IReadOnlyList<SubjectStudentRow> VisibleStudents
    {
        get => _visibleStudents;
        set => SetProperty(ref _visibleStudents, value);
    }

    private bool _hasMoreStudents;

    /// <summary>排名人数超过折叠阈值，需要显示展开 / 收起按钮。</summary>
    public bool HasMoreStudents
    {
        get => _hasMoreStudents;
        set => SetProperty(ref _hasMoreStudents, value);
    }

    private string _listToggleText = string.Empty;

    /// <summary>展开 / 收起按钮的文案；无折叠时为空。</summary>
    public string ListToggleText
    {
        get => _listToggleText;
        set => SetProperty(ref _listToggleText, value);
    }
}

public sealed class SubjectStudentRow
{
    public int Rank { get; init; }
    public string RankText { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public string ExamNo { get; init; } = string.Empty;
    public string ScoreText { get; init; } = string.Empty;
    public string RateText { get; init; } = string.Empty;
    public string DeltaText { get; init; } = string.Empty;
    public IBrush DeltaBrush { get; init; } = Palette.Muted;
    public double FillWeight { get; init; }
    public double AverageWeight { get; init; }
}


/// <summary>多考走势图上的一个数据节点。</summary>
public sealed class ChartPoint
{
    public string ExamOrderText { get; init; } = string.Empty;
    public string ExamName { get; init; } = string.Empty;
    public double Value { get; init; }
    public string ValueText { get; init; } = string.Empty;
    public string DeltaText { get; init; } = string.Empty;
    public IBrush? DeltaBrush { get; init; }
    public bool IsAbsent { get; init; }
}

/// <summary>多考单科历次成绩对照矩阵行。</summary>
public sealed class SubjectTrajectoryRow
{
    public string SubjectName { get; init; } = string.Empty;
    public IReadOnlyList<string> ExamScoresText { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExamRanksText { get; init; } = Array.Empty<string>();
    public string ExamScoresSummary => ExamScoresText.Count > 0 ? string.Join(" → ", ExamScoresText) : "—";
    public string ExamRanksSummary => ExamRanksText.Count > 0 ? string.Join(" → ", ExamRanksText.Select(r => r == "—" ? "—" : $"第{r}名")) : "—";
    public string NetDeltaText { get; init; } = string.Empty;
    public IBrush NetDeltaBrush { get; init; } = Palette.Muted;
    public string TrendTag { get; init; } = string.Empty;
    public IBrush TrendTagBrush { get; init; } = Palette.Muted;
}

/// <summary>满 3 次考试激活的多考综合档案。</summary>
public sealed class MultiExamProfile
{
    public bool IsAvailable { get; init; }
    public int ExamCount { get; init; }
    public string OverallTrendTag { get; init; } = string.Empty;
    public IBrush OverallTrendBrush { get; init; } = Palette.Moss;
    public string OverallTrendDescription { get; init; } = string.Empty;

    public string HighestTotalText { get; init; } = "—";
    public string HighestTotalExam { get; init; } = "—";
    public string LowestTotalText { get; init; } = "—";
    public string LowestTotalExam { get; init; } = "—";
    public string TotalRangeText { get; init; } = "—";

    public string BestClassRankText { get; init; } = "—";
    public string BestClassRankExam { get; init; } = "—";
    public string LowestClassRankText { get; init; } = "—";
    public string LowestClassRankExam { get; init; } = "—";
    public string ClassRankRangeText { get; init; } = "—";

    public IReadOnlyList<ChartPoint> ScorePoints { get; init; } = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> ClassRankPoints { get; init; } = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> SchoolRankPoints { get; init; } = Array.Empty<ChartPoint>();
    public IReadOnlyList<SubjectTrajectoryRow> SubjectRows { get; init; } = Array.Empty<SubjectTrajectoryRow>();
}

/// <summary>考试管理列表中的单场考试会话条目。</summary>
public sealed class ExamSessionItem
{
    public int Order { get; init; }
    public string OrderTitle => $"第 {Order} 次考试";
    public string ExamName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }
}
