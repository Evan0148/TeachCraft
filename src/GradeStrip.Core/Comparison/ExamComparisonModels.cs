using System;
using System.Collections.Generic;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Comparison;

/// <summary>跨考对比基准。</summary>
public enum ComparisonBaseline
{
    /// <summary>与上一场考试对比（第 N 场对比第 N-1 场）。</summary>
    PreviousExam,

    /// <summary>与第 1 次基准考试对比（第 N 场对比第 1 场）。</summary>
    FirstExam,

    /// <summary>保留表格原始自带的进退步数据。</summary>
    OriginalFile
}

/// <summary>一场考试的会话记录（包含先后次序与原始数据）。</summary>
public sealed class ExamSession
{
    public required int Order { get; init; } // 1, 2, 3...
    public required string Label { get; init; } // "第 1 次考试"
    public required string SourcePath { get; init; }
    public required string SourceFileName { get; init; }
    public required ExamData Data { get; init; }

    /// <summary>该场考试下挂的智学网分析报表（班级成绩对比、试题分析等），可为空。</summary>
    public List<OverviewReport> Reports { get; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Data.ExamName) ? SourceFileName : Data.ExamName;
}

/// <summary>学生在某一场考试中的轨迹数据点。</summary>
public sealed class StudentTrajectoryPoint
{
    public required int ExamOrder { get; init; }
    public required string ExamName { get; init; }
    public double? TotalScore { get; init; }
    public int? ClassRank { get; init; }
    public int? SchoolRank { get; init; }
    public IReadOnlyDictionary<string, double?> SubjectScores { get; init; } = new Dictionary<string, double?>();
    public IReadOnlyDictionary<string, int?> SubjectClassRanks { get; init; } = new Dictionary<string, int?>();
    public IReadOnlyDictionary<string, int?> SubjectSchoolRanks { get; init; } = new Dictionary<string, int?>();

    public bool IsAbsent => !TotalScore.HasValue && SubjectScores.Values.All(v => !v.HasValue);
}

/// <summary>跨期走势趋势分类。</summary>
public enum TrajectoryTrendType
{
    /// <summary>稳步攀升（名次显著提高，总分向上）。</summary>
    Rising,

    /// <summary>高位稳定（名次长期处于前列，波动小）。</summary>
    StableHigh,

    /// <summary>起伏波动（排名或分数反复震荡）。</summary>
    Fluctuating,

    /// <summary>阶段下滑（近期名次出现较明显倒退）。</summary>
    Declining,

    /// <summary>数据不足以判定趋势。</summary>
    InsufficientData
}

/// <summary>单科的历次走势明细。</summary>
public sealed class SubjectTrajectoryItem
{
    public required string SubjectName { get; init; }
    public required IReadOnlyList<double?> Scores { get; init; }
    public required IReadOnlyList<int?> ClassRanks { get; init; }
    public required IReadOnlyList<int?> SchoolRanks { get; init; }
    public double? NetScoreDelta { get; init; }
    public int? NetClassRankDelta { get; init; } // 正数代表名次前进 (如 10 -> 4 为 +6)
    public TrajectoryTrendType Trend { get; init; }
    public string TrendDescription { get; init; } = string.Empty;
}

/// <summary>学生完整多考轨迹与统计指标。</summary>
public sealed class StudentTrajectory
{
    public required string StudentName { get; init; }
    public required IReadOnlyList<StudentTrajectoryPoint> Points { get; init; }

    public double? HighestTotal { get; init; }
    public string HighestTotalExam { get; init; } = "—";
    public double? LowestTotal { get; init; }
    public string LowestTotalExam { get; init; } = "—";
    public double? TotalRange { get; init; }

    public int? BestClassRank { get; init; }
    public string BestClassRankExam { get; init; } = "—";
    public int? LowestClassRank { get; init; }
    public string LowestClassRankExam { get; init; } = "—";
    public int? ClassRankRange { get; init; }

    public int? BestSchoolRank { get; init; }
    public int? LowestSchoolRank { get; init; }

    public TrajectoryTrendType OverallTrend { get; init; }
    public string OverallTrendDescription { get; init; } = string.Empty;

    public IReadOnlyList<SubjectTrajectoryItem> Subjects { get; init; } = Array.Empty<SubjectTrajectoryItem>();

    public bool HasEnoughData => Points.Count(p => !p.IsAbsent) >= 2;
    public bool HasFullThreeExams => Points.Count(p => !p.IsAbsent) >= 3;
}
