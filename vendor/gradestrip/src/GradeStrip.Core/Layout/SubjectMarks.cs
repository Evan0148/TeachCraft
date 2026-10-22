using System;
using System.Collections.Generic;
using GradeStrip.Core.Analysis;

namespace GradeStrip.Core.Layout;

/// <summary>
/// 各科满分推导服务。
/// 智学网等系统导出的成绩单中通常无各科满分列，
/// 本服务结合学段、各省市考务标准（如中考折分方案）以及班级实考最高分，
/// 实现「标准档位折分 + 未折分原始卷自动跃迁」的智能判定。
/// </summary>
public static class SubjectMarks
{
    // 中考官方标准折分档位（基于 850 分总分制）：
    // 语数英各 120，物理 100，化学 70，历史 80，政治 70，体育 70，生物 50，地理 50。
    // 梯级数组升序排列：第一个元素为官方中考折分满分；后续元素为未折分时的原始试卷满分（如 100/120/150）。
    private static readonly Dictionary<string, int[]> JuniorLadders = new(StringComparer.Ordinal)
    {
        ["语文"] = new[] { 120, 150 },
        ["数学"] = new[] { 120, 150 },
        ["英语"] = new[] { 120, 150 },
        ["物理"] = new[] { 100 },
        ["化学"] = new[] { 70, 100 },
        ["历史"] = new[] { 80, 100 },
        ["政治"] = new[] { 70, 100 },
        ["道德与法治"] = new[] { 70, 100 },
        ["道法"] = new[] { 70, 100 },
        ["生物学"] = new[] { 50, 100 },
        ["生物"] = new[] { 50, 100 },
        ["地理"] = new[] { 50, 100 },
        ["体育"] = new[] { 70, 100 },
        ["体育与健康"] = new[] { 70, 100 },
        ["科学"] = new[] { 100, 160, 180, 200 },
    };

    // 高中（新高考 3+1+2 / 3+3）各科满分档位：
    // 语数外 150 分，物理/化学/生物/政治/历史/地理 均为 100 分。
    private static readonly Dictionary<string, int[]> SeniorLadders = new(StringComparer.Ordinal)
    {
        ["语文"] = new[] { 150 },
        ["数学"] = new[] { 150 },
        ["英语"] = new[] { 150 },
        ["外语"] = new[] { 150 },
        ["物理"] = new[] { 100 },
        ["化学"] = new[] { 100 },
        ["生物"] = new[] { 100 },
        ["生物学"] = new[] { 100 },
        ["政治"] = new[] { 100 },
        ["思想政治"] = new[] { 100 },
        ["历史"] = new[] { 100 },
        ["地理"] = new[] { 100 },
        ["技术"] = new[] { 100 },
        ["信息技术"] = new[] { 100 },
        ["通用技术"] = new[] { 100 },
        ["体育"] = new[] { 100 },
    };

    public const int Fallback = 100;

    /// <summary>
    /// 推导单科满分：
    /// 1. 根据学段和科目名称获取标准分值梯级池（如化学折分 70，未折分 100）；
    /// 2. 若传入班级最高分 highestObserved，寻找大于等于最高分的最小标准满分档位（未折分自动跃迁）；
    /// 3. 若最高分超过所有预设候选，则向上适配标准离散台阶（100, 120, 150, 200, 300）。
    /// </summary>
    public static int FullMark(string subject, double? highestObserved = null, SchoolStage stage = SchoolStage.General)
    {
        var cleanSubject = subject.Trim();
        var ladder = GetSubjectLadder(cleanSubject, stage);

        if (highestObserved is null or <= 0)
            return ladder[0];

        var highest = highestObserved.Value;

        // 寻找大于或等于最高实考分数的最小标准档位
        foreach (var tier in ladder)
        {
            if (highest <= tier)
                return tier;
        }

        // 超出预设梯级上限（如综合大卷 200/300 分等特殊情况），向上寻找离散大档位
        var extraTiers = new[] { 100, 120, 150, 200, 300 };
        foreach (var tier in extraTiers)
        {
            if (highest <= tier)
                return tier;
        }

        return (int)(Math.Ceiling(highest / 10d) * 10);
    }

    /// <summary>支持 GradeInfo 传入的重载</summary>
    public static int FullMark(string subject, double? highestObserved, GradeInfo? grade) =>
        FullMark(subject, highestObserved, grade?.Stage ?? SchoolStage.General);

    public static int TotalFullMark(
        IEnumerable<string> subjects,
        IReadOnlyDictionary<string, double>? highest = null,
        SchoolStage stage = SchoolStage.General)
    {
        var total = 0;
        foreach (var subject in subjects)
        {
            var h = highest is not null && highest.TryGetValue(subject, out var val) ? val : (double?)null;
            total += FullMark(subject, h, stage);
        }
        return total;
    }

    private static int[] GetSubjectLadder(string subject, SchoolStage stage)
    {
        if (stage == SchoolStage.Primary)
            return new[] { 100 };

        if (stage == SchoolStage.SeniorHigh)
        {
            if (SeniorLadders.TryGetValue(subject, out var seniorLadder))
                return seniorLadder;
            return new[] { 100, 150 };
        }

        // JuniorHigh 或 General 默认按初中（中考方案）标准
        if (JuniorLadders.TryGetValue(subject, out var juniorLadder))
            return juniorLadder;

        return new[] { 100, 120, 150 };
    }
}
