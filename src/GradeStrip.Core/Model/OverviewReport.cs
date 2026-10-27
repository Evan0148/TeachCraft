namespace GradeStrip.Core.Model;

/// <summary>智学网「分析报表」的大类。入口分流时据此决定是否走报表通道。</summary>
public enum OverviewReportKind
{
    Generic, ClassComparison, SubjectMetrics, ScoreSegments, GradeLevels,
    SubjectAdvantage, ClassAverages, TopBottomStats, BorderlineRoster,
    TopBottomRoster, AbsenceRoster, AttentionRoster, QuestionAnalysis,
    ChoiceDistribution
}

/// <summary>报表中的一列：表头文字与是否为数值列（人数、均分、占比等）。</summary>
public sealed class OverviewColumn
{
    public string Header { get; init; } = "";
    public bool IsNumeric { get; init; }
}

/// <summary>报表里的一张工作表。Rows 已按列数补齐，单元格为去除首尾空白的原始文本。</summary>
public sealed class OverviewSheet
{
    public string Name { get; init; } = "";
    public string? Subject { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<OverviewColumn> Columns { get; init; } = Array.Empty<OverviewColumn>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
}

/// <summary>一份智学网分析报表（一个文件对应一份，内部可含多张工作表）。</summary>
public sealed class OverviewReport
{
    public OverviewReportKind Kind { get; init; }
    public string KindLabel { get; init; } = "";
    public string Title { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public string? ExamName { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<OverviewSheet> Sheets { get; init; } = Array.Empty<OverviewSheet>();
}
