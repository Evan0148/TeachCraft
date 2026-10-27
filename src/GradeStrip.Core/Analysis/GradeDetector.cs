using System;
using System.Text.RegularExpressions;

namespace GradeStrip.Core.Analysis;

public enum SchoolStage
{
    Primary,    // 小学
    JuniorHigh, // 初中
    SeniorHigh, // 高中
    General     // 通用/未识别
}

public enum GradeLevel
{
    Primary1, Primary2, Primary3, Primary4, Primary5, Primary6,
    Junior7,  // 初一 / 七年级
    Junior8,  // 初二 / 八年级
    Junior9,  // 初三 / 九年级
    Senior1,  // 高一
    Senior2,  // 高二
    Senior3,  // 高三
    Unknown
}

public sealed record GradeInfo(
    GradeLevel Level,
    SchoolStage Stage,
    string DisplayName,        // 如 "九年级 (初三)"
    string ShortName,          // 如 "九年级"
    string StagePhaseTag,      // 如 "中考决胜冲刺期"
    string GuidanceFocus       // 核心教研指导导向
);

public static class GradeDetector
{
    private static readonly (string Pattern, GradeLevel Level, SchoolStage Stage, string Display, string Short, string Phase, string Focus)[] Rules =
    {
        // 高中
        (@"高三|高3\b|十二年级|高三年级", GradeLevel.Senior3, SchoolStage.SeniorHigh, "高三年级", "高三", "新高考冲刺总复习期", "聚焦高考赋分优化、特控线/本科线临界生攻坚与主观题规范答题"),
        (@"高二|高2\b|十一年级|高二年级", GradeLevel.Senior2, SchoolStage.SeniorHigh, "高二年级", "高二", "选科分化与深化期", "注重专业化学科知识建构、题型深度拓展与学考全员过关"),
        (@"高一|高1\b|十年级|高一年级", GradeLevel.Senior1, SchoolStage.SeniorHigh, "高一年级", "高一", "初高思维衔接奠基期", "弥补初高中抽象思维断层、规范解题步骤与平衡文理选科倾向"),

        // 初中
        (@"九年级|初三|九\d+班|初3\b", GradeLevel.Junior9, SchoolStage.JuniorHigh, "九年级 (初三)", "九年级", "中考决胜冲刺期", "注重中考总分木桶效应、理化实验与综合题突破、普高与重点高中临界生精准托底"),
        (@"八年级|初二|八\d+班|初2\b", GradeLevel.Junior8, SchoolStage.JuniorHigh, "八年级 (初二)", "八年级", "初中关键分水岭期", "严防两极分化与腰部坍塌、关注新增物理学科入门适应、强化几何逻辑推理"),
        (@"七年级|初一|七\d+班|初1\b", GradeLevel.Junior7, SchoolStage.JuniorHigh, "七年级 (初一)", "七年级", "新初中衔接适应期", "夯实学科基础、培养审题与草稿规范、引导多学科时间分配与良好学习习惯"),

        // 小学
        (@"六年级|小六|六\d+班", GradeLevel.Primary6, SchoolStage.Primary, "六年级", "六年级", "小升初准备期", "注重初小知识衔接、强化综合运算与篇章阅读理解能力"),
        (@"五年级|五\d+班", GradeLevel.Primary5, SchoolStage.Primary, "五年级", "五年级", "小学高学段", "深化抽象概念理解、培养探究性学习与自主预习复习习惯"),
        (@"四年级|四\d+班", GradeLevel.Primary4, SchoolStage.Primary, "四年级", "四年级", "小学中年段", "抓牢乘除运算与基础语法、关注思维灵活性"),
        (@"三年级|三\d+班", GradeLevel.Primary3, SchoolStage.Primary, "三年级", "三年级", "小学转折期", "克服三年级现象、稳固数学应用题与语文段落写作基础"),
        (@"二年级|二\d+班", GradeLevel.Primary2, SchoolStage.Primary, "二年级", "二年级", "小学低学段", "培养专注力与计算熟练度、强化字词积累"),
        (@"一年级|一\d+班", GradeLevel.Primary1, SchoolStage.Primary, "一年级", "一年级", "幼小衔接适应期", "重视学习兴趣激发、听讲习惯与规范书写姿势"),
    };

    /// <summary>
    /// 从考试名、班级名、源标题与文件名中多维度综合探测所属年级。
    /// </summary>
    public static GradeInfo Detect(params string?[] candidates)
    {
        foreach (var text in candidates)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (var rule in Rules)
            {
                if (Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase))
                {
                    return new GradeInfo(rule.Level, rule.Stage, rule.Display, rule.Short, rule.Phase, rule.Focus);
                }
            }
        }

        // 默认通用中学
        return new GradeInfo(
            GradeLevel.Unknown,
            SchoolStage.General,
            "中学通用",
            "本年级",
            "教学阶段学情诊断",
            "注重夯实基础知识体系、提高课堂思维参与度并实行分层分类精准辅导"
        );
    }
}
