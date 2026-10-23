namespace GradeStrip.Core.Model;

/// <summary>单题得分明细：题号 + 该生此题得分（null 表示缺答/未知）。</summary>
public sealed record QuestionScore(string Label, double? Score);

/// <summary>单个科目的成绩与排名信息。缺失值统一为 null。</summary>
public sealed class SubjectScore
{
    public double? Score { get; init; }
    public int? SchoolRank { get; init; }
    public int? SchoolDelta { get; init; }
    public int? ClassRank { get; init; }
    public int? ClassDelta { get; init; }

    public bool HasScore => Score.HasValue;

    public static readonly SubjectScore Empty = new();
}

/// <summary>一名学生的全部成绩。</summary>
public sealed class StudentRecord
{
    public int No { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ExamNo { get; init; } = string.Empty;

    /// <summary>科目名 -> 成绩。</summary>
    public Dictionary<string, SubjectScore> Subjects { get; init; } = new();

    /// <summary>得分明细（来自单科个人成绩单的题号/得分行，不含总分列）。null 表示本次导入无明细。</summary>
    public IReadOnlyList<QuestionScore>? QuestionScores { get; init; }

    public SubjectScore Total { get; set; } = SubjectScore.Empty;

    public SubjectScore this[string subject] =>
        Subjects.TryGetValue(subject, out var s) ? s : SubjectScore.Empty;
}

/// <summary>一次考试的完整数据。</summary>
public sealed class ExamData
{
    public string SourceTitle { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string ExamName { get; init; } = string.Empty;
    public IReadOnlyList<string> Subjects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<StudentRecord> Students { get; init; } = Array.Empty<StudentRecord>();

    /// <summary>导入提示信息（如简表缺少单科进退步、工作表回退提示等）。</summary>
    public string? ImportNotice { get; set; }

    public bool HasImportNotice => !string.IsNullOrWhiteSpace(ImportNotice);

    /// <summary>成绩条大标题，如「九6班 · 2026-2027学年第一学期九年级第一次段考 · 个人成绩条」。</summary>
    public string DefaultTitle =>
        string.Join(" · ", new[] { ClassName, ExamName, "个人成绩条" }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
