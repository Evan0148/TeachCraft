using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeStrip.Core.Layout;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Analysis;

/// <summary>
/// 年级专属的学科深度学情诊断与教学策略引擎。
/// 结合统计学分布、中考/高考教学常模及各年级心理认知规律生成精准教研洞察。
/// </summary>
public static class SubjectDiagnosisEngine
{
    public static SubjectDiagnosis Analyze(ExamData data, string subjectName, GradeInfo grade)
    {
        var scored = data.Students
            .Where(s => s[subjectName].Score.HasValue)
            .OrderByDescending(s => s[subjectName].Score!.Value)
            .ToList();

        if (scored.Count == 0)
        {
            return new SubjectDiagnosis
            {
                Grade = grade,
                SubjectName = subjectName,
                MacroAssessment = "本科目暂无有效参考学生成绩。",
                PolarizationAssessment = "无数据。",
                BorderlineAssessment = "无数据。",
                ActionStrategies = new[] { "等待录入考试数据后生成教研诊断。" },
                ComprehensiveSummary = "暂无数据。",
            };
        }

        var full = SubjectMarks.FullMark(subjectName, scored.Max(s => s[subjectName].Score), grade);
        var count = scored.Count;
        var avg = scored.Average(s => s[subjectName].Score!.Value);
        var avgRate = full > 0 ? avg / full : 0;
        var highest = scored[0][subjectName].Score!.Value;
        var lowest = scored[^1][subjectName].Score!.Value;
        var range = highest - lowest;

        // 尖子生名单
        var topStudents = scored.Where(s => s[subjectName].Score == highest)
            .Select(s => s.Name)
            .Take(3)
            .ToList();
        var topStudentText = string.Join("、", topStudents);

        // 达标线划分
        var passLine = full * 0.60;
        var passCount = scored.Count(s => s[subjectName].Score >= passLine);
        var passRate = (double)passCount / count;

        var excLine = full * 0.85;
        var excCount = scored.Count(s => s[subjectName].Score >= excLine);
        var excRate = (double)excCount / count;

        var goodLine = full * 0.70;
        var goodCount = scored.Count(s => s[subjectName].Score >= goodLine && s[subjectName].Score < excLine);
        var goodRate = (double)goodCount / count;

        // 临界生挖掘 (Borderline analysis)
        // 及格临界生: 55% ~ 65% 满分区间的学生 (最容易通过针对性辅导拉过及格线)
        var passBorderMin = full * 0.55;
        var passBorderMax = full * 0.65;
        var passBorderStudents = scored
            .Where(s => s[subjectName].Score >= passBorderMin && s[subjectName].Score <= passBorderMax)
            .Select(s => s.Name)
            .ToList();

        // 优秀临界生: 80% ~ 85% 满分区间
        var excBorderMin = full * 0.80;
        var excBorderMax = full * 0.85;
        var excBorderStudents = scored
            .Where(s => s[subjectName].Score >= excBorderMin && s[subjectName].Score < excBorderMax)
            .Select(s => s.Name)
            .ToList();

        // 全科得分率排行
        var subjectRankings = data.Subjects
            .Select(sub =>
            {
                var subScores = data.Students.Select(s => s[sub].Score).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                var subFull = SubjectMarks.FullMark(sub, subScores.Count > 0 ? subScores.Max() : null, grade);
                var subAvg = subScores.Count > 0 ? subScores.Average() : 0;
                var rate = subFull > 0 ? subAvg / subFull : 0;
                return (Name: sub, Rate: rate);
            })
            .OrderByDescending(x => x.Rate)
            .ToList();

        var subjectRank = subjectRankings.FindIndex(x => x.Name == subjectName) + 1;
        var isTopThird = subjectRank <= Math.Max(1, data.Subjects.Count / 3);
        var isBottomThird = subjectRank > data.Subjects.Count * 2 / 3;

        // 分学段与年级深度剖析
        string macroText;
        string polarText;
        string borderText;
        var strategies = new List<string>();

        switch (grade.Level)
        {
            case GradeLevel.Junior9: // 九年级 / 初三
                macroText = $"【九年级中考决胜视角】本科目均分 {avg:0.0} 分（均分得分率 {avgRate * 100:0.0}%），全班及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    (passRate >= 0.90
                        ? " 及格率达到 90% 以上高位，为中考普高升学构筑了坚实的托底盘；"
                        : passRate >= 0.75
                            ? " 及格率处于中游，面临普高升学边缘的踩线压力；"
                            : " 及格率偏低，已显现中考升学‘木桶短板’危机，可能直接拉低学生综合升学位次；") +
                    (isTopThird ? " 本科在全科中得分率位列前茅，是班级中考总分的强劲增长引擎。" :
                     isBottomThird ? " 本科在全科中表现靠后，是拖累班级总均分的突出学科，需列为教研重点攻坚阵地。" :
                     " 本科在各科中发挥均衡稳健。");

                polarText = $"全班极差达 {range:0.0} 分（最高分 {highest:0.0}，最低分 {lowest:0.0}）。" +
                    (range > full * 0.55
                        ? " 出现显著的两极分化，高分拔尖生与低分掉队生分水岭加剧，中考两头拉扯现象严重。"
                        : " 分数集中度较好，梯队过渡较为平顺，中坚生基本盘扎实。");

                borderText = $"【中考临界生档案】重点锁定 {passBorderStudents.Count} 名及格临界生（{FormatNames(passBorderStudents)}），" +
                             $"中考冲刺期提分性价比最高；另有 {excBorderStudents.Count} 名特优冲刺临界生（{FormatNames(excBorderStudents)}），是冲击重点高中指标到校的核心突破群。";

                strategies.Add($"拔尖突破：针对以 {topStudentText} 为代表的 {excCount} 名高分生，主攻中考压轴综合题思维拆解，强化规范答题与解题速度。");
                strategies.Add($"临界攻坚：对 {passBorderStudents.Count} 名及格临界生建立‘一人一档’错题会诊，重点排查选择填空等中考基础小题的失分点，确保及格分颗粒归仓。");
                strategies.Add("应试规范：九年级复习务必强化网上阅卷答题卡涂写、理科主观题步骤分及文科关键词采分点培训，杜绝隐性失分。");
                break;

            case GradeLevel.Junior8: // 八年级 / 初二
                macroText = $"【八年级初中分水岭视角】本科目均分 {avg:0.0} 分（得分率 {avgRate * 100:0.0}%），及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    (goodRate + excRate >= 0.60
                        ? " 良好以上学生占比达 " + $"{(goodRate + excRate) * 100:0.0}%" + "，班级整体思维素养成功经受住了初二知识难度跃升的检验。"
                        : " 良好层比例偏低，初二中坚‘腰部力量’出现收缩态势，需警惕分化下滑。");

                polarText = $"极差 {range:0.0} 分（最高 {highest:0.0}，最低 {lowest:0.0}）。" +
                    (range > full * 0.50
                        ? " 显著暴露出八年级‘两极分化’典型特征，后进生已显现跟不上课堂节奏的迹象。"
                        : " 班级离散程度受控，尚未形成不可逆的大面积断层。");

                borderText = $"检测到及格边缘生 {passBorderStudents.Count} 人（{FormatNames(passBorderStudents)}），" +
                             $"八年级是防掉队的生死线，一旦彻底滑落将在九年级难以补救；优秀储备生 {excBorderStudents.Count} 人。";

                strategies.Add("控尾防滑：八年级切忌赶教学进度，必须给中后段学生留出消化概念的时间，严防对本学科产生习得性无助或厌学情绪。");
                strategies.Add("抓实作业：实施分层弹性作业，基础题全员达标，拔高题鼓励探索，设立生生结对互助小组。");
                strategies.Add("思维进阶：针对逻辑思维门槛题（如数学几何证明、物理实验探究），强化‘画图审题’与‘模型归纳’习惯。");
                break;

            case GradeLevel.Junior7: // 七年级 / 初一
                macroText = $"【七年级新初中奠基视角】均分 {avg:0.0} 分（得分率 {avgRate * 100:0.0}%），及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    " 七年级重点考查对初中新增学科知识体系与阅读广度的适应水平，当前及格率反映了全班基础习惯的养成成效。";

                polarText = $"最高分 {highest:0.0}，最低分 {lowest:0.0}，极差 {range:0.0} 分。" +
                    (lowest < passLine * 0.7
                        ? " 低分段学生可能存在小学初中衔接不良、审题粗心或书写失分严重的现象。"
                        : " 全班整体适应节奏良好，学习信心基础扎实。");

                borderText = $"有 {passBorderStudents.Count} 名学生徘徊在及格线边缘（{FormatNames(passBorderStudents)}），初一阶段基础漏洞最小，及时补牢成本最低。";

                strategies.Add("规范立行：七年级关键在规矩与习惯，重点抓课堂笔记、草稿纸使用规范与课后作业自主独立订正。");
                strategies.Add("激发兴趣：结合学科生活情境与实验探究，多给予中下生展示成功的机会，保护学习热情。");
                strategies.Add("阅读思维：文科扩充群文阅读，理科注重数学语言表达准确性，克服小学生‘只写答案不写推理过程’的惯性。");
                break;

            case GradeLevel.Senior3: // 高三
                macroText = $"【高三年级高考总复习视角】均分 {avg:0.0} 分（得分率 {avgRate * 100:0.0}%），及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    (isTopThird ? " 在全班选考科目中位居前列，具备明显的赋分与总分贡献优势；" :
                     isBottomThird ? " 得分率相对全科偏低，属于高考总分中的拉分科目，亟需靶向突破；" : " 处于高考梯队的中坚平稳位次；") +
                    " 高考看位次与赋分，当前数据反映了该学科在高三首轮/二轮复习中的知识网覆盖扎实度。";

                polarText = $"极差达 {range:0.0} 分（最高分 {highest:0.0}，最低分 {lowest:0.0}）。学科区分度强，高分段具备良好的高考竞争壁垒。";

                borderText = $"特控/一本临界生与本科达线临界生合计 {passBorderStudents.Count + excBorderStudents.Count} 人，高考单科 3~5 分的突破即可能改变录取批次。";

                strategies.Add("真题拆解：严格以高考真题与课标为靶向，拆解高考高频命题模型与易错陷阱。");
                strategies.Add("限时训练：每周进行限时专项小卷训练，强化时间管理，避免考场上‘会做但来不及写完’。");
                strategies.Add("主观题踩点：强化主观论述与大题步骤的采分点意识，规范专业术语使用，消灭答非所问与表述不严谨。");
                break;

            case GradeLevel.Senior1:
            case GradeLevel.Senior2: // 高一/高二
                macroText = $"【高中进阶教学视角】均分 {avg:0.0} 分（得分率 {avgRate * 100:0.0}%），及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    " 考查高中学科知识体系的逻辑建构与高阶思维，为选科赋分与高考综合评价奠定基础。";

                polarText = $"极差 {range:0.0} 分（最高 {highest:0.0}，最低 {lowest:0.0}）。" +
                    (range > full * 0.5 ? " 学科知识抽象度升高后，部分学生出现思维断层，分化明显。" : " 学科梯队结构合理。");

                borderText = $"及格临界生 {passBorderStudents.Count} 人，良好至特优冲刺生 {excBorderStudents.Count} 人。";

                strategies.Add("图谱建构：引导学生每章绘制思维导图，把零散知识点串成网络，强化概念本质的理解。");
                strategies.Add("错题归因：落实错题本深度反思，分析是‘概念模糊’、‘审题漏看’还是‘计算失误’，进行针对性专项弥补。");
                break;

            default: // 通用
                macroText = $"【学情综合评价】满分 {full} 分，全班平均分 {avg:0.0} 分（得分率 {avgRate * 100:0.0}%），及格率 {passRate * 100:0.0}%，优秀率 {excRate * 100:0.0}%。" +
                    (isTopThird ? " 本科在全科得分率中位居前列，表现优异；" : isBottomThird ? " 本科得分率相对靠后，需加强教研帮扶；" : " 处于全科学科中间水平。");

                polarText = $"全班最高分 {highest:0.0} 分（{topStudentText}），最低分 {lowest:0.0} 分，极差 {range:0.0} 分。";

                borderText = $"发现及格临界生 {passBorderStudents.Count} 人，特优临界生 {excBorderStudents.Count} 人，具有明显的提分转化潜力。";

                strategies.Add("分类分层：实施分层目标教学，抓牢及格线基础题，同时为拔尖生设计高阶拓展练习。");
                strategies.Add("错题追踪：常态化开展错题过关检测，确保核心概念人人过关。");
                break;
        }

        var comprehensive = $"{macroText} {polarText} {borderText}";

        return new SubjectDiagnosis
        {
            Grade = grade,
            SubjectName = subjectName,
            MacroAssessment = macroText,
            PolarizationAssessment = polarText,
            BorderlineAssessment = borderText,
            ActionStrategies = strategies,
            ComprehensiveSummary = comprehensive,
        };
    }

    private static string FormatNames(List<string> names)
    {
        if (names.Count == 0) return "暂无集中群体";
        if (names.Count <= 3) return string.Join("、", names);
        return $"{names[0]}、{names[1]}等共{names.Count}人";
    }
}
