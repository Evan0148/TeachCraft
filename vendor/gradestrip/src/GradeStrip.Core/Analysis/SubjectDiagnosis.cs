using System;
using System.Collections.Generic;

namespace GradeStrip.Core.Analysis;

/// <summary>
/// 结构化的年级专属学科诊断结论。
/// </summary>
public sealed class SubjectDiagnosis
{
    public GradeInfo Grade { get; init; } = null!;
    public string SubjectName { get; init; } = string.Empty;

    /// <summary>年级背景与学段定位标签，如「九年级 · 中考决胜冲刺期」。</summary>
    public string GradeBadge => $"{Grade.DisplayName} · {Grade.StagePhaseTag}";

    /// <summary>宏观达标评估（均分、及格率与常模对比）。</summary>
    public string MacroAssessment { get; init; } = string.Empty;

    /// <summary>两极分化度评价（如「学情梯队密集、分布健康」、「极差偏大，两极分化显著」）。</summary>
    public string PolarizationAssessment { get; init; } = string.Empty;

    /// <summary>临界生诊断（及格线边缘、特优线边缘人数与名单建议）。</summary>
    public string BorderlineAssessment { get; init; } = string.Empty;

    /// <summary>教学与分层提分建议（拔尖生、临界生、后进生）。</summary>
    public IReadOnlyList<string> ActionStrategies { get; init; } = Array.Empty<string>();

    /// <summary>整合的一揽子诊断摘要，用于单卡片显示。</summary>
    public string ComprehensiveSummary { get; init; } = string.Empty;
}
