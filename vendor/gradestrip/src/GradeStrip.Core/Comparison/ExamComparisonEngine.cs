using System;
using System.Collections.Generic;
using System.Linq;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Comparison;

/// <summary>
/// 多次考试比对引擎：
/// 1. 负责在当前考试与指定基准考试之间计算精准的名次进退步（▲/▼）；
/// 2. 构建学生在历次考试中的综合发展轨迹与统计指标。
/// </summary>
public static class ExamComparisonEngine
{
    /// <summary>
    /// 将当前考试根据基准考试重新计算进退步。
    /// 班次与校次进退步定义：基准名次 - 当前名次（正数为进步 ▲，负数为退步 ▼）。
    /// </summary>
    public static ExamData CreateComparedExam(ExamData current, ExamData? baseline)
    {
        if (baseline is null)
            return current;

        var baseStudents = new Dictionary<string, StudentRecord>(StringComparer.Ordinal);
        foreach (var s in baseline.Students)
        {
            var key = s.Name.Trim();
            if (!string.IsNullOrEmpty(key) && !baseStudents.ContainsKey(key))
            {
                baseStudents[key] = s;
            }
        }

        var comparedStudents = new List<StudentRecord>(current.Students.Count);
        foreach (var student in current.Students)
        {
            var key = student.Name.Trim();
            baseStudents.TryGetValue(key, out var baseStudent);

            // 1. 重新计算总分进退步
            int? newClassDelta = null;
            if (student.Total.ClassRank is { } curClassRank &&
                baseStudent?.Total.ClassRank is { } baseClassRank)
            {
                newClassDelta = baseClassRank - curClassRank;
            }

            int? newSchoolDelta = null;
            if (student.Total.SchoolRank is { } curSchoolRank &&
                baseStudent?.Total.SchoolRank is { } baseSchoolRank)
            {
                newSchoolDelta = baseSchoolRank - curSchoolRank;
            }

            var newTotal = new SubjectScore
            {
                Score = student.Total.Score,
                ClassRank = student.Total.ClassRank,
                SchoolRank = student.Total.SchoolRank,
                ClassDelta = newClassDelta,
                SchoolDelta = newSchoolDelta
            };

            // 2. 重新计算各单科进退步
            var newSubjects = new Dictionary<string, SubjectScore>(StringComparer.Ordinal);
            foreach (var kv in student.Subjects)
            {
                var subName = kv.Key;
                var curSub = kv.Value;

                int? subClassDelta = null;
                int? subSchoolDelta = null;

                if (baseStudent is not null && baseStudent.Subjects.TryGetValue(subName, out var baseSub))
                {
                    if (curSub.ClassRank is { } curR && baseSub.ClassRank is { } baseR)
                    {
                        subClassDelta = baseR - curR;
                    }

                    if (curSub.SchoolRank is { } curSr && baseSub.SchoolRank is { } baseSr)
                    {
                        subSchoolDelta = baseSr - curSr;
                    }
                }

                newSubjects[subName] = new SubjectScore
                {
                    Score = curSub.Score,
                    ClassRank = curSub.ClassRank,
                    SchoolRank = curSub.SchoolRank,
                    ClassDelta = subClassDelta,
                    SchoolDelta = subSchoolDelta
                };
            }

            comparedStudents.Add(new StudentRecord
            {
                No = student.No,
                Name = student.Name,
                ExamNo = student.ExamNo,
                Total = newTotal,
                Subjects = newSubjects
            });
        }

        return new ExamData
        {
            SourceTitle = current.SourceTitle,
            ClassName = current.ClassName,
            ExamName = current.ExamName,
            Subjects = current.Subjects,
            Students = comparedStudents,
            ImportNotice = current.ImportNotice
        };
    }

    /// <summary>
    /// 构建学生在多场考试（>=2 或 >=3）中的完整发展轨迹。
    /// </summary>
    public static StudentTrajectory BuildStudentTrajectory(
        string studentName,
        IReadOnlyList<ExamSession> sessions)
    {
        var cleanName = studentName.Trim();
        var orderedSessions = sessions.OrderBy(s => s.Order).ToList();
        var points = new List<StudentTrajectoryPoint>(orderedSessions.Count);

        var allSubjects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in orderedSessions)
        {
            var student = session.Data.Students.FirstOrDefault(s => s.Name.Trim() == cleanName);

            var subScores = new Dictionary<string, double?>(StringComparer.Ordinal);
            var subClassRanks = new Dictionary<string, int?>(StringComparer.Ordinal);
            var subSchoolRanks = new Dictionary<string, int?>(StringComparer.Ordinal);

            if (student is not null)
            {
                foreach (var (sub, scoreObj) in student.Subjects)
                {
                    allSubjects.Add(sub);
                    subScores[sub] = scoreObj.Score;
                    subClassRanks[sub] = scoreObj.ClassRank;
                    subSchoolRanks[sub] = scoreObj.SchoolRank;
                }
            }

            points.Add(new StudentTrajectoryPoint
            {
                ExamOrder = session.Order,
                ExamName = session.DisplayName,
                TotalScore = student?.Total.Score,
                ClassRank = student?.Total.ClassRank,
                SchoolRank = student?.Total.SchoolRank,
                SubjectScores = subScores,
                SubjectClassRanks = subClassRanks,
                SubjectSchoolRanks = subSchoolRanks
            });
        }

        var validPoints = points.Where(p => p.TotalScore.HasValue).ToList();

        // 统计极值
        double? highestTotal = null;
        string highestTotalExam = "—";
        double? lowestTotal = null;
        string lowestTotalExam = "—";

        if (validPoints.Count > 0)
        {
            var maxP = validPoints.OrderByDescending(p => p.TotalScore!.Value).First();
            highestTotal = maxP.TotalScore;
            highestTotalExam = maxP.ExamName;

            var minP = validPoints.OrderBy(p => p.TotalScore!.Value).First();
            lowestTotal = minP.TotalScore;
            lowestTotalExam = minP.ExamName;
        }

        double? totalRange = (highestTotal.HasValue && lowestTotal.HasValue)
            ? highestTotal.Value - lowestTotal.Value
            : null;

        var rankedPoints = points.Where(p => p.ClassRank is > 0).ToList();
        int? bestClassRank = null;
        string bestClassRankExam = "—";
        int? lowestClassRank = null;
        string lowestClassRankExam = "—";

        if (rankedPoints.Count > 0)
        {
            var bestP = rankedPoints.OrderBy(p => p.ClassRank!.Value).First(); // 排名越小越优
            bestClassRank = bestP.ClassRank;
            bestClassRankExam = bestP.ExamName;

            var worstP = rankedPoints.OrderByDescending(p => p.ClassRank!.Value).First();
            lowestClassRank = worstP.ClassRank;
            lowestClassRankExam = worstP.ExamName;
        }

        int? classRankRange = (bestClassRank.HasValue && lowestClassRank.HasValue)
            ? lowestClassRank.Value - bestClassRank.Value
            : null;

        var schoolRankedPoints = points.Where(p => p.SchoolRank is > 0).ToList();
        int? bestSchoolRank = schoolRankedPoints.Count > 0 ? schoolRankedPoints.Min(p => p.SchoolRank!.Value) : null;
        int? lowestSchoolRank = schoolRankedPoints.Count > 0 ? schoolRankedPoints.Max(p => p.SchoolRank!.Value) : null;

        // 总体走势评价
        var (trend, trendDesc) = EvaluateTrend(rankedPoints.Select(p => p.ClassRank!.Value).ToList(), validPoints.Select(p => p.TotalScore!.Value).ToList());

        // 单科轨迹明细
        var subjectItems = new List<SubjectTrajectoryItem>();
        foreach (var sub in allSubjects.OrderBy(s => s))
        {
            var scores = points.Select(p => p.SubjectScores.TryGetValue(sub, out var sc) ? sc : null).ToList();
            var classRanks = points.Select(p => p.SubjectClassRanks.TryGetValue(sub, out var cr) ? cr : null).ToList();
            var schoolRanks = points.Select(p => p.SubjectSchoolRanks.TryGetValue(sub, out var sr) ? sr : null).ToList();

            var validScores = scores.Where(s => s.HasValue).Select(s => s!.Value).ToList();
            var validRanks = classRanks.Where(r => r.HasValue).Select(r => r!.Value).ToList();

            double? netScore = null;
            if (validScores.Count >= 2)
            {
                netScore = validScores[^1] - validScores[0];
            }

            int? netRank = null;
            if (validRanks.Count >= 2)
            {
                netRank = validRanks[0] - validRanks[^1]; // 首场名次 - 末场名次，正数代表上升
            }

            var (subTrend, subDesc) = EvaluateTrend(validRanks, validScores);

            subjectItems.Add(new SubjectTrajectoryItem
            {
                SubjectName = sub,
                Scores = scores,
                ClassRanks = classRanks,
                SchoolRanks = schoolRanks,
                NetScoreDelta = netScore,
                NetClassRankDelta = netRank,
                Trend = subTrend,
                TrendDescription = subDesc
            });
        }

        return new StudentTrajectory
        {
            StudentName = cleanName,
            Points = points,
            HighestTotal = highestTotal,
            HighestTotalExam = highestTotalExam,
            LowestTotal = lowestTotal,
            LowestTotalExam = lowestTotalExam,
            TotalRange = totalRange,
            BestClassRank = bestClassRank,
            BestClassRankExam = bestClassRankExam,
            LowestClassRank = lowestClassRank,
            LowestClassRankExam = lowestClassRankExam,
            ClassRankRange = classRankRange,
            BestSchoolRank = bestSchoolRank,
            LowestSchoolRank = lowestSchoolRank,
            OverallTrend = trend,
            OverallTrendDescription = trendDesc,
            Subjects = subjectItems
        };
    }

    private static (TrajectoryTrendType Type, string Desc) EvaluateTrend(
        IReadOnlyList<int> ranks,
        IReadOnlyList<double> scores)
    {
        if (ranks.Count < 2)
        {
            return (TrajectoryTrendType.InsufficientData, "单次考评 · 历次走势积累中");
        }

        var firstRank = ranks[0];
        var lastRank = ranks[^1];
        var netRankChange = firstRank - lastRank; // 正数进步
        var minRank = ranks.Min();
        var maxRank = ranks.Max();
        var rankSpread = maxRank - minRank;

        // 1. 高位稳定：一直在前 5 名或前 10 名，极差很小
        if (ranks.All(r => r <= 5) || (ranks.All(r => r <= 10) && rankSpread <= 3))
        {
            return (TrajectoryTrendType.StableHigh, "高位稳定 · 持续稳居班级第一梯队");
        }

        // 2. 稳步攀升：末场明显好于首场，且中间无暴跌
        if (netRankChange >= 4)
        {
            return (TrajectoryTrendType.Rising, $"稳步攀升 · 历次位次前进 {netRankChange} 名");
        }

        // 3. 阶段下滑：末场明显落后于首场
        if (netRankChange <= -5)
        {
            return (TrajectoryTrendType.Declining, $"阶段下滑 · 较首场后退 {-netRankChange} 名");
        }

        // 4. 起伏波动：极差较大但末尾与开头相差不大
        if (rankSpread >= 6)
        {
            return (TrajectoryTrendType.Fluctuating, $"起伏波动 · 位次在 {minRank} 至 {maxRank} 名间摆动");
        }

        return (TrajectoryTrendType.StableHigh, "表现平稳 · 班级位次保持稳定");
    }
}
